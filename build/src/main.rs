use std::fmt::{Display, Formatter};

use clap::{Parser, Subcommand};
use xtask::utils::log;
use xtask::{arch, utils};

#[derive(Parser, Debug)]
#[command(name = "xtask")]
#[command(version = "1.0.0")]
#[command(about = "Build system for Froststrap", long_about = None)]
struct AppArgs {
    #[command(subcommand)]
    /// Instruction on which to do with the Build System
    action: Action,
    #[arg(long, default_value_t = false)]
    /// Skip installers on publish
    no_installers: bool,
    #[arg(long, default_value_t = Configuration::default())]
    /// What configuration to use as a base
    configuration: Configuration,
    #[arg(long)]
    override_arch: Option<String>,
}

#[derive(Clone, Debug, Copy, PartialEq, Eq, clap::ValueEnum, Default)]
enum Configuration {
    Debug,
    #[default]
    Release,
}

impl Display for Configuration {
    fn fmt(&self, f: &mut Formatter<'_>) -> std::fmt::Result {
        use Configuration::*;
        f.write_str(match self {
            Debug => "debug",
            Release => "release",
        })
    }
}

#[derive(Subcommand, Debug)]
enum Action {
    Restore,
    Build,
    Publish,
}

pub fn main() {
    let args = AppArgs::parse();

    log::info!("{args:#?}");
    log::info!("hl: {}", arch::normalize_arch("arm64"));
    let cargo_v = utils::process("cargo", ["-V"]).exec().unwrap();

    println!("Cargo version: {}", cargo_v.stdout());
}
