//! Helpers for path finding

use crate::integrate::git;
use std::path::PathBuf;

const XTASK_DIR: &str = env!("CARGO_MANIFEST_DIR");

pub fn git_root() -> PathBuf {
    let git = git::info();
    git.dir.into()
}

pub fn xtask_root() -> PathBuf {
    PathBuf::from(XTASK_DIR)
}

pub fn output_root() -> PathBuf {
    git_root().join(".build")
}

pub fn dotnet_publish_artifacts() -> PathBuf {
    output_root().join("publish")
}
pub fn dotnet_build_artifacts() -> PathBuf {
    output_root().join("build")
}
pub fn bundling_artifacts() -> PathBuf {
    output_root().join("bundling")
}
pub fn dist() -> PathBuf {
    output_root().join("dist")
}
pub fn velopack() -> PathBuf {
    output_root().join("velopack")
}
