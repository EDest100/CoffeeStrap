//! helpers to be able to do hot
//! functions quick

use std::ffi::OsStr;
use std::io;
use std::process::Command;

pub mod log {
    use std::fmt;

    const RESET: &str = "\x1b[0m";
    pub const GRAY: &str = "\x1b[90m";
    pub const BLUE: &str = "\x1b[34m";
    pub const YELLOW: &str = "\x1b[33m";
    pub const RED: &str = "\x1b[31m";

    pub fn timestamp() -> String {
        use std::time::{SystemTime, UNIX_EPOCH};
        let secs = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_secs();
        let (h, m, s) = ((secs / 3600) % 24, (secs / 60) % 60, secs % 60);
        format!("{h:02}:{m:02}:{s:02}")
    }

    pub fn paint(color: &str, text: &str) -> String {
        format!("{color}{text}{RESET}")
    }

    pub struct Blue<T>(pub T);

    macro_rules! forward {
        ($($tr:ident),*) => {$(
            impl<T: fmt::$tr> fmt::$tr for Blue<T> {
                fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
                    f.write_str(BLUE)?;
                    fmt::$tr::fmt(&self.0, f)?;
                    f.write_str(RESET)
                }
            }
        )*};
    }
    forward!(Display, Debug, LowerHex, UpperHex, Binary);

    #[macro_export]
    macro_rules! __log {
        ($print:ident, $color:expr, $level:literal, $fmt:literal $(, $arg:expr)* $(,)?) => {
            $print!(
                "{} [{}] {}",
                $crate::utils::log::paint($crate::utils::log::GRAY, &$crate::utils::log::timestamp()),
                $crate::utils::log::paint($color, $level),
                format_args!($fmt $(, $crate::utils::log::Blue(&$arg))*)
            )
        };
    }

    #[macro_export]
    macro_rules! __info {
        ($($t:tt)*) => { $crate::__log!(println, $crate::utils::log::BLUE, "INF", $($t)*) };
    }

    #[macro_export]
    macro_rules! __warn {
        ($($t:tt)*) => { $crate::__log!(eprintln, $crate::utils::log::YELLOW, "WRN", $($t)*) };
    }

    #[macro_export]
    macro_rules! __error {
        ($($t:tt)*) => { $crate::__log!(eprintln, $crate::utils::log::RED, "ERR", $($t)*) };
    }

    pub use __error as error;
    pub use __info as info;
    pub use __warn as warn;
}

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
