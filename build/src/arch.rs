//! Handling for getting and serializing Arch details
//! for different build system.

use std::{env::consts, sync::RwLock};

static OVERRIDE_ARCH: RwLock<&'static str> = RwLock::new("");

pub fn target_arch() -> &'static str {
    if *OVERRIDE_ARCH.read().expect("lock poisoned") == "" {
        consts::ARCH
    } else {
        &*&OVERRIDE_ARCH.read().expect("lock poisoned")
    }
}

pub fn rust_triple() -> &'static str {
    match *OVERRIDE_ARCH.read().expect("lock poisoned") {
        "x86_64" if consts::OS == "macos" => "x86_64-apple-darwin",
        "aarch64" if consts::OS == "macos" => "aarch64-apple-darwin",
        "x86_64" if consts::OS == "windows" => "x86_64-pc-windows-msvc",
        "aarch64" if consts::OS == "windows" => "aarch64-pc-windows-msvc",
        "x86_64" if consts::OS == "linux" => "x86_64-unknown-linux-gnu",
        "aarch64" if consts::OS == "linux" => "aarch64-unknown-linux-gnu",
        _ => "unknown-unknown-unknown",
    }
}

pub fn dotnet_arch() -> &'static str {
    match *OVERRIDE_ARCH.read().expect("lock poisoned") {
        "x64" | "x86_64" | "x86-64" | "amd64" => "x64",
        "aarch64" | "arm64" => "arm64",
        _ => panic!("Unrecognized arch"),
    }
}

pub fn normalize_arch(arch: &str) -> &str {
    match arch {
        "x64" | "x86_64" | "x86-64" | "amd64" => "x86_64",
        "aarch64" | "arm64" => "aarch64",
        _ => panic!("Unrecognized arch"),
    }
}
