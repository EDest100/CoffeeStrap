//! Build system for Froststrap

pub mod action;
pub mod arch;
pub mod integrate;
pub mod path;
pub mod utils;

/// Arg handling
pub mod args {
    use clap::{Parser, Subcommand};
    use std::{
        fmt::{Display, Formatter},
        sync::OnceLock,
    };

    #[derive(Parser, Debug)]
    #[command(name = "xtask")]
    #[command(version = "1.0.0")]
    #[command(about = "Build system for Froststrap", long_about = None)]
    pub struct AppArgs {
        #[command(subcommand)]
        /// Instruction on which to do with the Build System
        pub action: Action,
        #[arg(long, default_value_t = false)]
        /// Skip installers on publish
        pub no_installers: bool,
        #[arg(long, default_value_t = Configuration::default())]
        /// What configuration to use as a base
        pub configuration: Configuration,
        #[arg(long)]
        /// Override to cross-compile arch's
        pub override_arch: Option<String>,
    }

    #[derive(Clone, Debug, Copy, PartialEq, Eq, clap::ValueEnum, Default)]
    pub enum Configuration {
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
    pub enum Action {
        Restore,
        Build,
        Publish,
    }

    static ARGS: OnceLock<AppArgs> = OnceLock::new();

    pub fn init() {
        let args = AppArgs::parse();
        ARGS.set(args).expect("args already initialized");
    }

    pub fn get() -> &'static AppArgs {
        ARGS.get().expect("args not initialized, call init() first")
    }
}
