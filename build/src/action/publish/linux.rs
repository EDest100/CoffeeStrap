//! os specific stuff for publish

use crate::args;

pub fn main() {
    let args = args::get();
    if args.no_installers {
        return;
    }
}
