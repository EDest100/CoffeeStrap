use xtask::{
    arch,
    utils::{self, log},
};

pub fn main() {
    xtask::args::init();

    log::info!("Running xtask {}", env!("CARGO_PKG_VERSION"));

    println!(); // seperator

    let g = xtask::integrate::git::info();
    log::info!("Git commit: {}", g.commit);
    log::info!("Git branch: {}", g.branch);
    log::info!("Git local dir: {}", g.dir);
    log::info!("Git tag: {}", g.tag.as_deref().unwrap_or("none"));

    println!(); // seperator

    let args = xtask::args::get();
    log::info!("No Installers: {}", args.no_installers);
    log::info!("Configuration: {}", args.configuration);

    println!(); // seperator

    log::info!("Target Arch: {}", arch::target_arch());
    log::info!("Rust Target: {}", arch::rust_triple());
    log::info!("Dotnet Target: {}", arch::dotnet_arch());

    let cargo_v = utils::process("cargo", ["-V"]).exec().unwrap();
    log::info!("Cargo version: {}", cargo_v.stdout());
}
