// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
#pragma once
#include <aclapi.h>
#include <sddl.h>
#include <versionhelpers.h>
#include <cstdint>
#include <cwchar>
#include <string>
#include <string_view>

extern "C" __declspec(dllexport) unsigned long CoffeeStrapUpscalerGuardVersion() { return 1; }

namespace UpscalerGuard {
struct OwnedHandle { HANDLE value = nullptr; ~OwnedHandle() { if (value) CloseHandle(value); } };
struct MutexLease { HANDLE value = nullptr; bool owned = false; ~MutexLease() { if (owned) ReleaseMutex(value); if (value) CloseHandle(value); } };
struct Security { PSECURITY_DESCRIPTOR value = nullptr; ~Security() { if (value) LocalFree(value); } };
struct Magnifier {
    using Initialize = BOOL(WINAPI*)(); using Show = BOOL(WINAPI*)(BOOL);
    HMODULE library = LoadLibraryExW(L"Magnification.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    Initialize initialize = library ? reinterpret_cast<Initialize>(GetProcAddress(library, "MagInitialize")) : nullptr;
    Initialize uninitialize = library ? reinterpret_cast<Initialize>(GetProcAddress(library, "MagUninitialize")) : nullptr;
    Show show = library ? reinterpret_cast<Show>(GetProcAddress(library, "MagShowSystemCursor")) : nullptr;
    bool active = false;
    ~Magnifier() { if (active) uninitialize(); if (library) FreeLibrary(library); }
};
inline bool requested(int count, wchar_t** values) {
    return count > 1 && std::wcscmp(values[1], L"--coffeestrap-upscale-guard") == 0;
}
inline bool number(std::wstring_view text, uint64_t maximum, uint64_t& value) {
    if (text.empty() || text.size() > 20) return false;
    value = 0;
    for (wchar_t digit : text) {
        if (digit < L'0' || digit > L'9' || value > maximum / 10 || (value == maximum / 10 && static_cast<uint64_t>(digit - L'0') > maximum % 10)) return false;
        value = value * 10 + static_cast<uint64_t>(digit - L'0');
    }
    return true;
}
inline bool coordinate(const wchar_t* text, LONG& result) {
    std::wstring_view value(text); bool negative = !value.empty() && value[0] == L'-';
    if (negative) value.remove_prefix(1);
    uint64_t parsed = 0;
    if (!number(value, negative ? 2147483648ULL : 2147483647ULL, parsed)) return false;
    result = static_cast<LONG>(negative ? -static_cast<int64_t>(parsed) : static_cast<int64_t>(parsed)); return true;
}
inline bool rectangle(wchar_t** values, RECT& result) {
    return coordinate(values[0], result.left) && coordinate(values[1], result.top) && coordinate(values[2], result.right) && coordinate(values[3], result.bottom)
        && result.left < result.right && result.top < result.bottom;
}
inline bool events(const wchar_t* ready, const wchar_t* stop, DWORD process) {
    std::wstring prefix = L"Local\\CoffeeStrap.Upscaler." + std::to_wstring(process) + L".";
    std::wstring first(ready), second(stop);
    if (first.size() != prefix.size() + 38 || first.compare(0, prefix.size(), prefix) != 0 || first.substr(first.size() - 6) != L".ready") return false;
    auto guid = first.substr(prefix.size(), 32);
    for (wchar_t digit : guid) if (!((digit >= L'0' && digit <= L'9') || (digit >= L'a' && digit <= L'f'))) return false;
    return second == prefix + guid + L".stop";
}
inline bool sameUserObject(HANDLE event, PSID user) {
    Security descriptor; PSID owner = nullptr;
    return GetSecurityInfo(event, SE_KERNEL_OBJECT, OWNER_SECURITY_INFORMATION, &owner, nullptr, nullptr, nullptr, &descriptor.value) == ERROR_SUCCESS
        && owner && EqualSid(owner, user);
}
inline std::wstring normalized(const wchar_t* path) {
    std::wstring value(path);
    if (value.rfind(L"\\\\?\\UNC\\", 0) == 0) value = L"\\\\" + value.substr(8);
    else if (value.rfind(L"\\\\?\\", 0) == 0) value = value.substr(4);
    wchar_t full[32768]{}; DWORD length = GetFullPathNameW(value.c_str(), 32768, full, nullptr);
    return length > 0 && length < 32768 ? std::wstring(full, length) : std::wstring();
}
inline int run(int count, wchar_t** values) {
    if (count != 14 || !IsWindows10OrGreater()) return 2;
    uint64_t pid = 0, started = 0;
    if (!number(values[2], MAXDWORD, pid) || pid == 0 || pid == GetCurrentProcessId() || !number(values[3], INT64_MAX, started) || started == 0) return 2;
    RECT oldClip{}, ownedClip{};
    if (!rectangle(values + 4, oldClip) || !rectangle(values + 8, ownedClip) || ownedClip.left < oldClip.left || ownedClip.top < oldClip.top
        || ownedClip.right > oldClip.right || ownedClip.bottom > oldClip.bottom || !events(values[12], values[13], static_cast<DWORD>(pid))) return 2;
    OwnedHandle parent{OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, static_cast<DWORD>(pid))};
    if (!parent.value) return 3;
    FILETIME creation{}, exit{}, kernel{}, user{};
    if (!GetProcessTimes(parent.value, &creation, &exit, &kernel, &user) || (static_cast<uint64_t>(creation.dwHighDateTime) << 32 | creation.dwLowDateTime) != started) return 3;
    wchar_t ownPath[32768]{}, parentPath[32768]{}; DWORD length = 32768;
    DWORD ownLength = GetModuleFileNameW(nullptr, ownPath, 32768);
    if (ownLength == 0 || ownLength >= 32768 || !QueryFullProcessImageNameW(parent.value, 0, parentPath, &length)) return 3;
    auto own = normalized(ownPath), other = normalized(parentPath);
    if (own.empty() || other.empty() || _wcsicmp(own.c_str(), other.c_str()) != 0) return 3;
    OwnedHandle token;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value)) return 4;
    alignas(TOKEN_USER) unsigned char tokenData[512]{}; DWORD returned = 0;
    if (!GetTokenInformation(token.value, TokenUser, tokenData, sizeof(tokenData), &returned)) return 4;
    PSID userSid = reinterpret_cast<TOKEN_USER*>(tokenData)->User.Sid;
    OwnedHandle ready{OpenEventW(EVENT_MODIFY_STATE | READ_CONTROL, FALSE, values[12])}, stop{OpenEventW(SYNCHRONIZE | READ_CONTROL, FALSE, values[13])};
    if (!ready.value || !stop.value || !sameUserObject(ready.value, userSid) || !sameUserObject(stop.value, userSid)) return 4;
    LPWSTR sid = nullptr;
    if (!ConvertSidToStringSidW(userSid, &sid)) return 8;
    Security sidText{sid}, mutexSecurity;
    std::wstring mutexName = L"Local\\CoffeeStrap.Upscaler.User." + std::wstring(sid);
    std::wstring permissions = L"O:" + std::wstring(sid) + L"D:P(A;;GA;;;" + sid + L")";
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(permissions.c_str(), SDDL_REVISION_1, &mutexSecurity.value, nullptr)) return 8;
    SECURITY_ATTRIBUTES attributes{sizeof(SECURITY_ATTRIBUTES), mutexSecurity.value, FALSE};
    MutexLease lease{CreateMutexW(&attributes, FALSE, mutexName.c_str())};
    if (!lease.value || !sameUserObject(lease.value, userSid)) return 8;
    DWORD acquired = WaitForSingleObject(lease.value, 0);
    if (acquired != WAIT_OBJECT_0 && acquired != WAIT_ABANDONED) return 8;
    lease.owned = true;
    Magnifier magnifier;
    if (!magnifier.initialize || !magnifier.uninitialize || !magnifier.show || !magnifier.initialize()) return 5;
    magnifier.active = true;
    if (acquired == WAIT_ABANDONED && !magnifier.show(TRUE)) return 5;
    if (!SetEvent(ready.value)) return 6;
    HANDLE handles[] = {parent.value, stop.value};
    DWORD wait = WaitForMultipleObjects(2, handles, FALSE, INFINITE);
    bool restored = magnifier.show(TRUE) != FALSE;
    RECT current{};
    if (!GetClipCursor(&current)) restored = false;
    else if (EqualRect(&current, &ownedClip) && !ClipCursor(&oldClip)) restored = false;
    if (!magnifier.uninitialize()) restored = false;
    magnifier.active = false;
    return (wait == WAIT_OBJECT_0 || wait == WAIT_OBJECT_0 + 1) && restored ? 0 : 7;
}
}
