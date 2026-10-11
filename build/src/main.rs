use xtask::utils::log;
use xtask::{arch, utils};

pub fn main() {
    xtask::args::init();
    let args = xtask::args::get();

    log::info!("Starting xtask {}", env!("CARGO_PKG_VERSION"));

    let g = xtask::integrate::git::info();
    log::info!("Git commit: {}", g.commit);
    log::info!("Git branch: {}", g.branch);
    log::info!("Git local dir: {}", g.dir);
    log::info!("Git tag: {}", g.tag.as_deref().unwrap_or("none"));

    log::info!("{args:#?}");
    log::info!("hl: {}", arch::normalize_arch("arm64"));
    let cargo_v = utils::process("cargo", ["-V"]).exec().unwrap();

    println!("Cargo version: {}", cargo_v.stdout());
}
