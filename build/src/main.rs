use xtask::{
    arch,
    utils::{self, log},
};

pub fn main() {
    xtask::args::init();

    log::info!("Running xtask {}", env!("CARGO_PKG_VERSION"));

    println!(); // seperator

    let git = xtask::integrate::git::info();
    log::info!("Git commit: {}", git.commit);
    log::info!("Git branch: {}", git.branch);
    log::info!("Git local dir: {}", git.dir);
    log::info!("Git tag: {}", git.tag.as_deref().unwrap_or("none"));

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

    args.action.exec();
}
