//! helpers to be able to do hot
//! functions quick

use std::ffi::OsStr;
use std::io;
use std::process::Command;

pub struct ProcessBuilder {
    cmd: Command,
}

pub struct Process {
    stdout: String,
    stderr: String,
    exit_code: Option<i32>,
}

pub fn process<I, S>(file_name: &str, args: I) -> ProcessBuilder
where
    I: IntoIterator<Item = S>,
    S: AsRef<OsStr>,
{
    let mut cmd = Command::new(file_name);
    cmd.args(args);
    ProcessBuilder { cmd }
}

impl ProcessBuilder {
    pub fn cwd(mut self, dir: impl AsRef<std::path::Path>) -> Self {
        self.cmd.current_dir(dir);
        self
    }

    pub fn env(mut self, k: impl AsRef<OsStr>, v: impl AsRef<OsStr>) -> Self {
        self.cmd.env(k, v);
        self
    }

    /// Runs the process to completion and captures everything.
    pub fn exec(mut self) -> io::Result<Process> {
        let out = self.cmd.output()?;
        Ok(Process {
            stdout: String::from_utf8_lossy(&out.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&out.stderr).into_owned(),
            exit_code: out.status.code(),
        })
    }
}

impl Process {
    pub fn stdout(&self) -> &str {
        &self.stdout
    }
    pub fn stderr(&self) -> &str {
        &self.stderr
    }
    pub fn exit_code(&self) -> Option<i32> {
        self.exit_code
    }
    pub fn success(&self) -> bool {
        self.exit_code == Some(0)
    }
}
