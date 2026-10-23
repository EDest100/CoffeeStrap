// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
#include "Host.cpp"
#include <fstream>
#include <iostream>
#include <functional>

static void append(std::vector<unsigned char>& data, uint64_t number, size_t bytes) {
    for (size_t i = 0; i < bytes; i++) data.push_back(static_cast<unsigned char>(number >> (i * 8)));
}
static std::vector<unsigned char> synthetic(const std::vector<std::string>& names, const std::string& entry = "CoffeeStrap.dll") {
    std::vector<unsigned char> metadata;
    for (const auto& name : names) {
        append(metadata, name.size(), 2); append(metadata, 0, 8); auto checksum = hash({});
        metadata.insert(metadata.end(), checksum.begin(), checksum.end()); metadata.insert(metadata.end(), name.begin(), name.end());
    }
    std::vector<unsigned char> result{'C','S','C','O','F','F','0','1'};
    append(result, 1, 4); append(result, 5, 4); append(result, names.size(), 4); append(result, metadata.size(), 4);
    append(result, 0, 8); append(result, 0, 8);
    for (const auto& digest : {hash(metadata), hash({})}) result.insert(result.end(), digest.begin(), digest.end());
    append(result, entry.size(), 2); result.insert(result.end(), entry.begin(), entry.end()); result.insert(result.end(), metadata.begin(), metadata.end()); return result;
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    std::ifstream input(fs::path(argv[1]), std::ios::binary);
    std::vector<unsigned char> original((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
    int checks = 0;
    auto require = [&](bool success, const std::string& name) { if (!success) throw std::runtime_error(name); ++checks; std::cout << "PASS: " << name << '\n'; };
    auto rejects = [&](const std::vector<unsigned char>& bytes, const std::string& name) { bool failed = false; try { Payload payload(bytes); } catch (const std::runtime_error&) { failed = true; } require(failed, name); };
    try {
        Payload valid(original); require(valid.files.size() > 20 && valid.entry == L"CoffeeStrap.dll", "real production payload parses");
        for (size_t size : {size_t(0), size_t(7), size_t(31), size_t(103), original.size() - 1}) rejects(std::vector<unsigned char>(original.begin(), original.begin() + size), "truncated payload " + std::to_string(size));
        for (const auto& item : std::vector<std::pair<size_t, std::string>>{{0,"magic"},{8,"version"},{12,"codec"},{16,"count"},{20,"manifest limit"},{24,"expanded size"},{32,"compressed size"},{40,"manifest digest"}}) {
            auto bad = original; for (size_t i = 0; i < 4; i++) bad[item.first + i] = 255; rejects(bad, "invalid " + item.second);
        }
        auto trailing = original; trailing.push_back(0); rejects(trailing, "trailing data");
        for (const auto& name : {"../CoffeeStrap.dll", "/CoffeeStrap.dll", "C:/CoffeeStrap.dll", "a/../CoffeeStrap.dll", "a\\CoffeeStrap.dll", "a./CoffeeStrap.dll", "a//CoffeeStrap.dll"}) rejects(synthetic({name}, name), "unsafe path " + std::string(name));
        rejects(synthetic({"CoffeeStrap.dll", "COFFEESTRAP.DLL"}), "case-insensitive destination collision");
        rejects(synthetic({"other.dll"}), "missing entry assembly");
        std::cout << checks << " native payload checks passed.\n"; return 0;
    }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
