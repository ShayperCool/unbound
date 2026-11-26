//! Launches of the Unity Editor's executable from the game's process.
//!
//! Game and editor code see the real Unity installation (`EditorApplication.applicationPath`
//! stays its executable: packages find the installation through it). Unbound never runs that
//! executable: the process's `execve` (which .NET's `Process.Start` calls in the new process) is
//! Unbound's own, exported from the executable, and looks at what is about to run:
//!
//! - a virtual player that Multiplayer Play Mode starts (`--virtual-project-clone`, its project
//!   folder as `-projectPath`) runs as another `unbound run` of that folder, with the player's
//!   arguments after `--` (they are its command line as editor code reads it) and its output in
//!   the `-logFile` the arguments name;
//! - any other launch of the Editor's executable is refused: the call fails with `EPERM` (so
//!   `Process.Start` throws) after a line on the new process's standard error.
//!
//! Everything else runs as asked. The function runs in the child of a `vfork`, so it only reads
//! memory, uses the stack and makes system calls.
use std::ffi::{CString, c_char, c_int};
use std::path::Path;
use std::sync::OnceLock;

struct Guard {
    /// The Editor's executable, as configured and lexically normalized (`..` and `.` resolved).
    unity: Vec<Vec<u8>>,
    /// This executable.
    unbound: CString,
}

static GUARD: OnceLock<Guard> = OnceLock::new();

/// Guards launches of `unity` (the Editor's executable) from now on. Call before game code runs.
/// Linux only: elsewhere a launch is not intercepted (said once here).
pub fn install(unity: &Path) {
    #[cfg(not(target_os = "linux"))]
    tracing::warn!(
        "Unbound does not intercept launches of the Unity Editor's executable on this OS: a Multiplayer Play Mode virtual player would start the real Editor"
    );
    let Ok(exe) = std::env::current_exe() else {
        tracing::warn!(
            "Unbound cannot name its own executable: launches of the Unity Editor's executable are refused"
        );
        return;
    };
    let mut forms = Vec::new();
    for form in [Some(unity.to_path_buf()), std::fs::canonicalize(unity).ok()]
        .into_iter()
        .flatten()
    {
        let mut buffer = [0u8; PATH_LIMIT];
        let bytes = form.as_os_str().as_encoded_bytes();
        if let Some(n) = normalize(bytes, &mut buffer) {
            let normalized = buffer[..n].to_vec();
            if !forms.contains(&normalized) {
                forms.push(normalized);
            }
        }
    }
    let Ok(unbound) = CString::new(exe.as_os_str().as_encoded_bytes()) else {
        return;
    };
    let _ = GUARD.set(Guard {
        unity: forms,
        unbound,
    });
}

const PATH_LIMIT: usize = 4096;
const ARG_LIMIT: usize = 512;

/// `path` with `.` and `..` components and repeated slashes resolved, into `out`: its length, or
/// None for a relative path or one too long.
fn normalize(path: &[u8], out: &mut [u8; PATH_LIMIT]) -> Option<usize> {
    if path.first() != Some(&b'/') {
        return None;
    }
    let mut len = 0usize;
    for component in path.split(|&b| b == b'/') {
        match component {
            b"" | b"." => {}
            b".." => {
                while len > 0 && out[len - 1] != b'/' {
                    len -= 1;
                }
                len = len.saturating_sub(1);
            }
            name => {
                if len + 1 + name.len() > PATH_LIMIT {
                    return None;
                }
                out[len] = b'/';
                out[len + 1..len + 1 + name.len()].copy_from_slice(name);
                len += 1 + name.len();
            }
        }
    }
    if len == 0 {
        out[0] = b'/';
        len = 1;
    }
    Some(len)
}

/// The bytes of a C string (without its terminator).
unsafe fn bytes<'a>(s: *const c_char) -> &'a [u8] {
    unsafe { std::ffi::CStr::from_ptr(s).to_bytes() }
}

/// What a launch of the Editor's executable with these arguments becomes.
#[derive(Debug, PartialEq)]
enum Launch {
    /// A virtual player: `unbound run` of its project folder (the argument at `project`), its
    /// output into the argument at `log`, if any.
    Clone { project: usize, log: Option<usize> },
    /// Anything else: refused.
    Refused,
}

/// Reads the arguments (without the program's own, `argv[0]`) of a launch of the Editor.
fn classify(args: &[&[u8]]) -> Launch {
    let mut clone = false;
    let mut project = None;
    let mut log = None;
    for (i, arg) in args.iter().enumerate() {
        match *arg {
            b"--virtual-project-clone" => clone = true,
            b"-projectPath" | b"-projectpath" if i + 1 < args.len() => project = Some(i + 1),
            b"-logFile" | b"-logfile" if i + 1 < args.len() => log = Some(i + 1),
            _ => {}
        }
    }
    match (clone, project) {
        (true, Some(project)) => Launch::Clone { project, log },
        _ => Launch::Refused,
    }
}

const RUN: &[u8] = b"run\0";
const PROJECT: &[u8] = b"-p\0";
const NO_WATCH: &[u8] = b"--no-watch\0";
const REST: &[u8] = b"--\0";

/// # Safety
/// The C `execve`: `path` a C string, `argv` and `envp` null-terminated arrays of C strings.
#[cfg(target_os = "linux")]
#[unsafe(no_mangle)]
pub unsafe extern "C" fn execve(
    path: *const c_char,
    argv: *const *const c_char,
    envp: *const *const c_char,
) -> c_int {
    unsafe {
        if let Some(guard) = GUARD.get()
            && !path.is_null()
            && is_unity(guard, bytes(path))
        {
            return launch(guard, argv, envp);
        }
        real_execve(path, argv, envp)
    }
}

#[cfg(target_os = "linux")]
unsafe fn real_execve(
    path: *const c_char,
    argv: *const *const c_char,
    envp: *const *const c_char,
) -> c_int {
    unsafe { libc::syscall(libc::SYS_execve, path, argv, envp) as c_int }
}

fn is_unity(guard: &Guard, path: &[u8]) -> bool {
    let mut buffer = [0u8; PATH_LIMIT];
    let Some(n) = normalize(path, &mut buffer) else {
        return false;
    };
    guard
        .unity
        .iter()
        .any(|form| form.as_slice() == &buffer[..n])
}

#[cfg(target_os = "linux")]
unsafe fn launch(guard: &Guard, argv: *const *const c_char, envp: *const *const c_char) -> c_int {
    unsafe {
        // The arguments, without the program's own.
        let mut args: [&[u8]; ARG_LIMIT] = [&[]; ARG_LIMIT];
        let mut pointers: [*const c_char; ARG_LIMIT] = [std::ptr::null(); ARG_LIMIT];
        let mut count = 0usize;
        if !argv.is_null() && !(*argv).is_null() {
            let mut at = argv.add(1);
            while !(*at).is_null() {
                if count == ARG_LIMIT {
                    return refuse(b"too many arguments");
                }
                args[count] = bytes(*at);
                pointers[count] = *at;
                count += 1;
                at = at.add(1);
            }
        }
        let Launch::Clone { project, log } = classify(&args[..count]) else {
            return refuse(
                b"only Multiplayer Play Mode's virtual players run (as another Unbound)",
            );
        };
        if let Some(log) = log {
            let fd = libc::open(
                pointers[log],
                libc::O_WRONLY | libc::O_CREAT | libc::O_APPEND,
                0o644,
            );
            if fd >= 0 {
                libc::dup2(fd, 1);
                libc::dup2(fd, 2);
                if fd > 2 {
                    libc::close(fd);
                }
            }
        }
        // unbound run -p <project> --no-watch -- <the player's arguments>
        let mut run: [*const c_char; ARG_LIMIT + 8] = [std::ptr::null(); ARG_LIMIT + 8];
        run[0] = guard.unbound.as_ptr();
        run[1] = RUN.as_ptr().cast();
        run[2] = PROJECT.as_ptr().cast();
        run[3] = pointers[project];
        run[4] = NO_WATCH.as_ptr().cast();
        run[5] = REST.as_ptr().cast();
        run[6..6 + count].copy_from_slice(&pointers[..count]);
        real_execve(guard.unbound.as_ptr(), run.as_ptr(), envp)
    }
}

#[cfg(target_os = "linux")]
unsafe fn refuse(why: &[u8]) -> c_int {
    const PREFIX: &[u8] = b"Unbound: refusing to run the Unity Editor's executable: ";
    unsafe {
        libc::write(2, PREFIX.as_ptr().cast(), PREFIX.len());
        libc::write(2, why.as_ptr().cast(), why.len());
        libc::write(2, b"\n".as_ptr().cast(), 1);
        *libc::__errno_location() = libc::EPERM;
    }
    -1
}

#[cfg(test)]
mod tests {
    use super::*;

    fn norm(path: &str) -> Option<String> {
        let mut buffer = [0u8; PATH_LIMIT];
        normalize(path.as_bytes(), &mut buffer)
            .map(|n| String::from_utf8(buffer[..n].to_vec()).unwrap())
    }

    #[test]
    fn paths_are_compared_with_dots_resolved() {
        assert_eq!(
            norm("/opt/Unity/Editor/Data/../Unity").as_deref(),
            Some("/opt/Unity/Editor/Unity")
        );
        assert_eq!(norm("/a//b/./c/").as_deref(), Some("/a/b/c"));
        assert_eq!(norm("/..").as_deref(), Some("/"));
        assert_eq!(norm("relative/Unity"), None);
    }

    #[test]
    fn a_virtual_player_becomes_a_run_of_its_folder_and_anything_else_is_refused() {
        let args: Vec<&[u8]> = vec![
            b"-projectPath",
            b"/p/Library/VP/abc",
            b"--virtual-project-clone",
            b"-logFile",
            b"/p/Logs/Editor-abc.txt",
            b"-vpId=abc",
        ];
        assert_eq!(
            classify(&args),
            Launch::Clone {
                project: 1,
                log: Some(4)
            }
        );
        let plain: Vec<&[u8]> = vec![b"-projectPath", b"/p", b"-batchmode"];
        assert_eq!(classify(&plain), Launch::Refused);
        let no_project: Vec<&[u8]> = vec![b"--virtual-project-clone"];
        assert_eq!(classify(&no_project), Launch::Refused);
    }
}
