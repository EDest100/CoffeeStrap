//! Handling for getting and serializing Arch details
//! for different build system.

use std::{env::consts, sync::RwLock};

static OVERRIDE_ARCH: RwLock<&'static str> = RwLock::new("");

pub fn target_arch() -> String {
    if let Some(a) = &crate::args::get().override_arch {
        return a.clone();
    }

    let o = OVERRIDE_ARCH.read().expect("lock poisoned");
    if o.is_empty() {
        consts::ARCH.to_string()
    } else {
        o.to_string()
    }
}

/// Always "x86_64" or "aarch64".
pub fn normalized_arch() -> &'static str {
    normalize_arch(&target_arch())
}

pub fn rust_triple() -> &'static str {
    match (normalized_arch(), consts::OS) {
        ("x86_64", "macos") => "x86_64-apple-darwin",
        ("aarch64", "macos") => "aarch64-apple-darwin",
        ("x86_64", "windows") => "x86_64-pc-windows-msvc",
        ("aarch64", "windows") => "aarch64-pc-windows-msvc",
        ("x86_64", "linux") => "x86_64-unknown-linux-gnu",
        ("aarch64", "linux") => "aarch64-unknown-linux-gnu",
        _ => "unknown-unknown-unknown",
    }
}

pub fn dotnet_arch() -> &'static str {
    match normalized_arch() {
        "x86_64" => "x64",
        "aarch64" => "arm64",
        _ => unreachable!(),
    }
}

/// Maps the many spellings of an arch to a better name.
pub fn normalize_arch(arch: &str) -> &'static str {
    match arch {
        "x64" | "x86_64" | "x86-64" | "amd64" => "x86_64",
        "aarch64" | "arm64" => "aarch64",
        other => panic!("Unrecognized arch: {other}"),
    }
}
