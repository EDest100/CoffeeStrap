//! Module holding all "actions" avalailable
//!
//! Export the module with a main function.

pub mod clean;
pub mod build;
pub mod restore;
pub mod publish;

use clap::Subcommand;

#[derive(Subcommand, Debug, Copy, Clone)]
pub enum Action {
    Clean,
    Restore,
    Build,
    Publish,
}

impl Action {
    pub fn exec(self) {
        match self {
            Action::Build => build::main(),
            Action::Restore => restore::main(),
            Action::Clean => clean::main(),
            Action::Publish => publish::main(),
        }
    }
}
