// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define NETHOST_USE_AS_STATIC
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <compressapi.h>
#include <bcrypt.h>
#include <winioctl.h>
#include <nethost.h>
#include <hostfxr.h>
#include <array>
#include <filesystem>
#include <span>
#include <string>
#include <vector>
#include <stdexcept>
#include <algorithm>
#include <memory>
#include <cstdio>

namespace fs = std::filesystem;
using Bytes = std::span<const unsigned char>;
using Digest = std::array<unsigned char, 32>;
struct Handle { HANDLE value; ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); } };
struct Apartment { HRESULT result = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); ~Apartment() { if (SUCCEEDED(result)) CoUninitialize(); } };
struct Arguments { int count = 0; wchar_t** values = CommandLineToArgvW(GetCommandLineW(), &count); ~Arguments() { if (values) LocalFree(values); } };
struct Decompressor { DECOMPRESSOR_HANDLE value = nullptr; ~Decompressor() { if (value) CloseDecompressor(value); } };
struct Reader {
    Bytes bytes; size_t position = 0;
    Bytes take(size_t size) { if (size > bytes.size() - position) throw std::runtime_error("Truncated application payload."); auto result = bytes.subspan(position, size); position += size; return result; }
    template<class T> T number() { T value; auto data = take(sizeof(T)); memcpy(&value, data.data(), sizeof(T)); return value; }
};
static Digest hash(Bytes bytes) {
    Digest result{};
    if (BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0, const_cast<PUCHAR>(bytes.data()), static_cast<ULONG>(bytes.size()), result.data(), 32) < 0) throw std::runtime_error("Cannot verify application data.");
    return result;
}
static std::wstring hex(const Digest& digest) {
    std::wstring value; constexpr wchar_t digits[] = L"0123456789abcdef";
    for (auto byte : digest) { value += digits[byte >> 4]; value += digits[byte & 15]; } return value;
}
static Digest digest(Reader& input) { Digest value{}; auto bytes = input.take(32); std::copy(bytes.begin(), bytes.end(), value.begin()); return value; }
static std::wstring pathName(Bytes bytes) {
    if (bytes.empty() || bytes.size() > 512) throw std::runtime_error("Invalid application file name.");
    std::wstring value;
    for (unsigned char c : bytes) {
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || c == '/')) throw std::runtime_error("Unsafe application file name.");
        value += static_cast<wchar_t>(c);
    }
    fs::path relative(value);
    if (relative.has_root_path() || value.find(L"//") != std::wstring::npos || value.back() == L'/') throw std::runtime_error("Unsafe application file path.");
    for (const auto& component : relative) {
        auto name = component.wstring();
        if (name.empty() || name == L"." || name == L".." || name.back() == L'.') throw std::runtime_error("Unsafe application path component.");
    }
    return value;
}
static bool reparse(const fs::path& path) {
    DWORD flags = GetFileAttributesW(path.c_str()); return flags != INVALID_FILE_ATTRIBUTES && (flags & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
}
static fs::path extended(fs::path path) {
    auto value = fs::absolute(path).lexically_normal().make_preferred().wstring();
    if (value.rfind(L"\\\\?\\", 0) == 0) return value;
    if (value.rfind(L"\\\\", 0) == 0) return L"\\\\?\\UNC\\" + value.substr(2);
    return L"\\\\?\\" + value;
}
static fs::path resolve(const fs::path& root, const std::wstring& name) { return (root / name).make_preferred(); }
static void directory(const fs::path& path, const fs::path& root) {
    if (path != root) directory(path.parent_path(), root);
    if (reparse(path)) throw std::runtime_error("An application cache path is a link. Remove the link before retrying.");
    if (!CreateDirectoryW(path.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) throw std::runtime_error("Cannot create the application cache directory.");
    if (!fs::is_directory(path)) throw std::runtime_error("The application cache path is not a directory.");
}
struct File { std::wstring name; uint64_t size; Digest checksum; size_t offset; };
struct Payload {
    uint32_t algorithm; uint64_t size; Digest checksum; std::wstring entry; Bytes packed; std::vector<File> files; std::wstring identity;
    explicit Payload(Bytes bytes) {
        Reader reader{bytes}; auto magic = reader.take(8);
        if (memcmp(magic.data(), "CSCOFF01", 8) != 0 || reader.number<uint32_t>() != 1) throw std::runtime_error("Unknown application payload format.");
        algorithm = reader.number<uint32_t>(); uint32_t count = reader.number<uint32_t>(), manifestSize = reader.number<uint32_t>();
        size = reader.number<uint64_t>(); uint64_t packedSize = reader.number<uint64_t>();
        auto manifestHash = digest(reader); checksum = digest(reader);
        if ((algorithm != COMPRESS_ALGORITHM_XPRESS_HUFF && algorithm != COMPRESS_ALGORITHM_LZMS) || count == 0 || count > 1024 || manifestSize > 1024 * 1024 || size > 128 * 1024 * 1024 || packedSize > 128 * 1024 * 1024) throw std::runtime_error("Application payload limits exceeded.");
        entry = pathName(reader.take(reader.number<uint16_t>()));
        auto metadata = reader.take(manifestSize); if (hash(metadata) != manifestHash) throw std::runtime_error("Application manifest verification failed.");
        Reader manifest{metadata}; size_t total = 0;
        for (uint32_t i = 0; i < count; i++) {
            auto nameSize = manifest.number<uint16_t>(); auto length = manifest.number<uint64_t>(); auto fileHash = digest(manifest);
            auto name = pathName(manifest.take(nameSize));
            if (length > 64 * 1024 * 1024 || length > size - total || std::any_of(files.begin(), files.end(), [&](const File& f) { return _wcsicmp(f.name.c_str(), name.c_str()) == 0; })) throw std::runtime_error("Invalid application file manifest.");
            files.push_back({name, length, fileHash, total}); total += static_cast<size_t>(length);
        }
        if (manifest.position != metadata.size() || total != size || !std::any_of(files.begin(), files.end(), [&](const File& file) { return file.name == entry; })) throw std::runtime_error("Incomplete application manifest.");
        packed = reader.take(static_cast<size_t>(packedSize)); if (reader.position != bytes.size()) throw std::runtime_error("Unexpected trailing application data.");
        identity = hex(hash(bytes));
    }
    bool validFile(const fs::path& path, const File& file) const {
        if (reparse(path)) throw std::runtime_error("An application cache file is a link.");
        Handle handle{CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr)};
        if (handle.value == INVALID_HANDLE_VALUE) {
            if (GetLastError() == ERROR_FILE_NOT_FOUND || GetLastError() == ERROR_PATH_NOT_FOUND) return false;
            throw std::runtime_error("Cannot read a cached application file. Close other CoffeeStrap windows and retry.");
        }
        LARGE_INTEGER length{}; if (!GetFileSizeEx(handle.value, &length) || static_cast<uint64_t>(length.QuadPart) != file.size) return false;
        std::vector<unsigned char> bytes(static_cast<size_t>(file.size)); DWORD read = 0;
        if (!ReadFile(handle.value, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr) || read != bytes.size()) throw std::runtime_error("Cannot verify a cached application file.");
        return hash(bytes) == file.checksum;
    }
    fs::path prepare(const fs::path& root) const {
        fs::path target = root / identity;
        directory(root, root); directory(target, root);
        Handle mutex{CreateMutexW(nullptr, FALSE, (L"Local\\CoffeeStrapPayload-" + identity).c_str())};
        if (!mutex.value) throw std::runtime_error("Cannot lock the application cache.");
        DWORD wait = WaitForSingleObject(mutex.value, 30000);
        if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) throw std::runtime_error("Another CoffeeStrap window is preparing this build. Try again shortly.");
        struct Unlock { HANDLE handle; ~Unlock() { ReleaseMutex(handle); } } unlock{mutex.value};
        if ((GetFileAttributesW(target.c_str()) & FILE_ATTRIBUTE_COMPRESSED) == 0) {
            Handle folder{CreateFileW(target.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr)};
            if (folder.value != INVALID_HANDLE_VALUE) {
                USHORT format = COMPRESSION_FORMAT_LZNT1; DWORD returned = 0;
                DeviceIoControl(folder.value, FSCTL_SET_COMPRESSION, &format, sizeof(format), nullptr, 0, &returned, nullptr);
            }
        }
        for (const auto& file : files) directory(resolve(target, file.name).parent_path(), root);
        std::vector<const File*> missing;
        for (const auto& file : files) if (!validFile(resolve(target, file.name), file)) missing.push_back(&file);
        if (!missing.empty()) {
            Decompressor decompressor;
            if (!CreateDecompressor(algorithm, nullptr, &decompressor.value)) throw std::runtime_error("Windows compression could not be initialized.");
            std::vector<unsigned char> data(static_cast<size_t>(size)); SIZE_T actual = 0;
            BOOL ok = Decompress(decompressor.value, packed.data(), packed.size(), data.data(), data.size(), &actual);
            if (!ok || actual != size || hash(data) != checksum) throw std::runtime_error("The application payload is damaged. Download CoffeeStrap again.");
            for (const File* file : missing) {
                auto destination = resolve(target, file->name);
                ULONG nonce = 0;
                if (BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(&nonce), sizeof(nonce), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) throw std::runtime_error("Cannot create a unique cache staging name.");
                auto temporary = destination.parent_path() / (L".pending-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(nonce));
                Handle output{CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr)};
                if (output.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot stage an application cache file.");
                DWORD written = 0;
                BOOL saved = WriteFile(output.value, data.data() + file->offset, static_cast<DWORD>(file->size), &written, nullptr) && written == file->size && FlushFileBuffers(output.value);
                CloseHandle(output.value); output.value = INVALID_HANDLE_VALUE;
                if (!saved || !MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
                    DeleteFileW(temporary.c_str()); throw std::runtime_error("Cannot replace a cached application file. Close other CoffeeStrap windows and retry.");
                }
            }
        }
        if (!missing.empty()) for (const auto& file : files) if (!validFile(resolve(target, file.name), file)) throw std::runtime_error("Final application file verification failed.");
        for (const auto& item : fs::recursive_directory_iterator(target)) {
            if (reparse(item.path())) throw std::runtime_error("Unexpected link in the application cache.");
            auto extension = item.path().extension().wstring();
            if (item.is_regular_file() && (_wcsicmp(extension.c_str(), L".dll") == 0 || _wcsicmp(extension.c_str(), L".json") == 0)) {
                auto relative = item.path().lexically_relative(target).generic_wstring();
                if (!std::any_of(files.begin(), files.end(), [&](const File& file) { return _wcsicmp(file.name.c_str(), relative.c_str()) == 0; })) throw std::runtime_error("Unexpected application library in the cache. Choose an empty COFFEESTRAP_RUNTIME_CACHE directory and retry.");
            }
        }
        return target;
    }
};
static int run(const Payload& payload, const fs::path& cache, const fs::path& executable, int argc, wchar_t** argv) {
    auto entry = (cache / payload.entry).wstring();
    get_hostfxr_parameters options{sizeof(get_hostfxr_parameters), entry.c_str(), nullptr};
    size_t length = 0; get_hostfxr_path(nullptr, &length, &options);
    if (length == 0 || length > 32768) throw std::runtime_error("Install the .NET 10 x64 runtime from https://dotnet.microsoft.com/download/dotnet/10.0/runtime, then try again.");
    std::vector<wchar_t> path(length);
    if (get_hostfxr_path(path.data(), &length, &options) != 0) throw std::runtime_error("The .NET host could not be located. Install the .NET 10 x64 runtime.");
    HMODULE library = LoadLibraryExW(path.data(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!library) throw std::runtime_error("The installed .NET host could not be loaded.");
    auto initialize = reinterpret_cast<hostfxr_initialize_for_dotnet_command_line_fn>(GetProcAddress(library, "hostfxr_initialize_for_dotnet_command_line"));
    auto runApp = reinterpret_cast<hostfxr_run_app_fn>(GetProcAddress(library, "hostfxr_run_app"));
    auto close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(library, "hostfxr_close"));
    if (!initialize || !runApp || !close) throw std::runtime_error("The installed .NET host is incompatible.");
    auto dotnetRoot = fs::path(path.data()).parent_path().parent_path().parent_path().parent_path().wstring();
    auto outer = executable.wstring(); hostfxr_initialize_parameters parameters{sizeof(hostfxr_initialize_parameters), outer.c_str(), dotnetRoot.c_str()};
    std::vector<const wchar_t*> arguments{entry.c_str()}; for (int i = 1; i < argc; i++) arguments.push_back(argv[i]);
    hostfxr_handle context = nullptr;
    int result = initialize(static_cast<int>(arguments.size()), arguments.data(), &parameters, &context);
    if (result < 0 || !context) throw std::runtime_error("CoffeeStrap requires the .NET 10 x64 runtime. The installed runtime could not start this build.");
    result = runApp(context); close(context); return result;
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    try {
        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        auto resource = FindResourceW(instance, MAKEINTRESOURCEW(301), MAKEINTRESOURCEW(10));
        if (!resource) throw std::runtime_error("The embedded application is missing.");
        auto loaded = LoadResource(instance, resource); auto size = SizeofResource(instance, resource);
        if (!loaded || size > 128 * 1024 * 1024) throw std::runtime_error("Invalid embedded application size.");
        auto address = static_cast<const unsigned char*>(LockResource(loaded));
        if (!address || size == 0) throw std::runtime_error("The application payload could not be read.");
        Payload payload(Bytes(address, size));
        std::vector<wchar_t> custom(32768); DWORD customSize = GetEnvironmentVariableW(L"COFFEESTRAP_RUNTIME_CACHE", custom.data(), static_cast<DWORD>(custom.size()));
        fs::path cacheRoot;
        if (customSize > 0) {
            if (customSize >= custom.size() || !fs::path(custom.data()).is_absolute()) throw std::runtime_error("COFFEESTRAP_RUNTIME_CACHE must be an absolute local directory.");
            cacheRoot = extended(custom.data());
            if (!fs::is_directory(cacheRoot.parent_path()) || reparse(cacheRoot.parent_path())) throw std::runtime_error("The custom runtime-cache parent must be an existing directory without links.");
        }
        else {
            PWSTR local = nullptr; if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local))) throw std::runtime_error("The Windows user profile is unavailable.");
            fs::path base = extended(local); CoTaskMemFree(local);
            directory(base / L"CoffeeStrap", base); cacheRoot = base / L"CoffeeStrap" / L"RuntimeCache";
        }
        auto cache = payload.prepare(cacheRoot);
        std::vector<wchar_t> executable(32768); DWORD length = GetModuleFileNameW(nullptr, executable.data(), static_cast<DWORD>(executable.size()));
        if (length == 0 || length >= executable.size()) throw std::runtime_error("The launcher path could not be resolved.");
        Arguments arguments;
        if (!arguments.values) throw std::runtime_error("The launcher arguments could not be decoded.");
        Apartment apartment;
        if (FAILED(apartment.result)) throw std::runtime_error("The Windows UI thread could not be initialized.");
        return run(payload, cache, executable.data(), arguments.count, arguments.values);
    }
    catch (const std::exception& error) {
        HANDLE stderrHandle = GetStdHandle(STD_ERROR_HANDLE);
        if (stderrHandle && stderrHandle != INVALID_HANDLE_VALUE) { DWORD written = 0; auto text = std::string("CoffeeStrap: ") + error.what() + "\r\n"; WriteFile(stderrHandle, text.data(), static_cast<DWORD>(text.size()), &written, nullptr); }
        else MessageBoxA(nullptr, error.what(), "CoffeeStrap could not start", MB_OK | MB_ICONERROR);
        return 1;
    }
}
