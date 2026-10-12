//! Build system for Froststrap

pub mod action;
pub mod arch;
pub mod integrate;
pub mod path;
pub mod utils;
pub mod args;

use utils::log;
use integrate::git;

pub fn main() {
    args::init();

    log::info!("Running xtask {}", env!("CARGO_PKG_VERSION"));

    println!(); // seperator

    let git = git::info();
    log::info!("Git commit: {}", git.commit);
    log::info!("Git branch: {}", git.branch);
    log::info!("Git local dir: {}", git.dir);
    log::info!("Git tag: {}", git.tag.as_deref().unwrap_or("none"));

    println!(); // seperator

    let args = args::get();
    log::info!("No Installers: {}", args.no_installers);
    log::info!("Configuration: {}", args.configuration);

    println!(); // seperator

    log::info!("Target Arch: {}", arch::target_arch());
    log::info!("Rust Target: {}", arch::rust_triple());
    log::info!("Dotnet Target: {}", arch::dotnet_arch());

    let cargo_v = utils::process("cargo", ["-V"]).exec().unwrap();
    log::info!("Cargo version: {}", cargo_v.stdout());

    args.action.exec();
}
