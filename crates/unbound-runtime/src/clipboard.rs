//! The system clipboard behind `GUIUtility.systemCopyBuffer` and
//! `EditorGUIUtility.systemCopyBuffer` (6000.0 Scripting API: "Get access to the system-wide
//! clipboard."): text only, through arboard. The managed engine asks with the `clipboard-get` and
//! `clipboard-set` control calls; it keeps its own in-process copy for when they fail.
//!
//! Ownership follows arboard 3.7.0's `Clipboard` documentation:
//! - On X11 and Wayland the text this process copies is served by this process, so "when the
//!   last `Clipboard` instance is dropped, the contents may become unavailable to other apps".
//!   One `Clipboard` is therefore opened on the first call and kept for the session; a call never
//!   opens a throwaway one.
//! - "All `Clipboard`s must be 'dropped' before the program exits", and under frameworks that
//!   take over the event loop (winit) "you have to make sure the object is dropped" when the
//!   application is about to quit. [`release`] drops it (on X11 that hands what we own to the
//!   clipboard manager, waiting at most 100 ms); it must run on the quit path, after the game's
//!   own quit callbacks. Calls after it fail, so the managed side keeps its in-process copy.
//! - A `Clipboard` is `Send + Sync`, but parallel operations "might fail with a
//!   `ClipboardOccupied` error": every call goes through one lock. The calls come from the
//!   managed caller (the game's main thread); the audio and render threads never touch it.
use anyhow::{Result, anyhow};
use std::sync::{Mutex, MutexGuard};

/// What reading the clipboard's text found.
#[derive(Debug, PartialEq, Eq)]
pub enum Read {
    /// UTF-8 text, exactly as the clipboard holds it (line ends included).
    Text(String),
    /// No text: the clipboard is empty or holds another format (arboard's `ContentNotAvailable`),
    /// or its text is not UTF-8 (`ConversionFailure`). Answered as `"text":null`, not a failure.
    NoText,
}

/// The system clipboard, text only. Tests stand in their own.
pub trait Backend: Send {
    fn read_text(&mut self) -> Result<Read>;
    fn write_text(&mut self, text: &str) -> Result<()>;
}

/// Opens the system clipboard: arboard's, or a test's stand-in.
pub type Open = fn() -> Result<Box<dyn Backend>>;

struct Arboard(arboard::Clipboard);

impl Backend for Arboard {
    fn read_text(&mut self) -> Result<Read> {
        classify_text(self.0.get_text())
    }

    fn write_text(&mut self, text: &str) -> Result<()> {
        self.0
            .set_text(text)
            .map_err(|error| anyhow!("writing the system clipboard failed: {error}"))
    }
}

/// arboard 3.7.0 also reports a selection returning an incompatible target as Unknown.
/// That reply contains no usable text, just like ContentNotAvailable. Other failures remain
/// errors so an unavailable clipboard is still diagnosed.
fn classify_text(result: std::result::Result<String, arboard::Error>) -> Result<Read> {
    match result {
        Ok(text) => Ok(Read::Text(text)),
        Err(arboard::Error::ContentNotAvailable | arboard::Error::ConversionFailure) => {
            Ok(Read::NoText)
        }
        Err(arboard::Error::Unknown { description })
            if description == "incorrect type received from clipboard" =>
        {
            Ok(Read::NoText)
        }
        Err(error) => Err(anyhow!("reading the system clipboard failed: {error}")),
    }
}

fn open_arboard() -> Result<Box<dyn Backend>> {
    let clipboard = arboard::Clipboard::new()
        .map_err(|error| anyhow!("the system clipboard could not be opened: {error}"))?;
    Ok(Box::new(Arboard(clipboard)))
}

enum Slot {
    /// Opened by the first call.
    Closed,
    Open(Box<dyn Backend>),
    /// Opening failed; not retried in this session (every call answers this reason).
    Unavailable(String),
    /// Dropped on the quit path; never opened again.
    Released,
}

/// The clipboard the control calls use: opened once, kept until [`Session::release`].
pub struct Session {
    open: Open,
    slot: Slot,
}

impl Session {
    pub const fn new(open: Open) -> Self {
        Self {
            open,
            slot: Slot::Closed,
        }
    }

    fn backend(&mut self) -> Result<&mut dyn Backend> {
        if matches!(self.slot, Slot::Closed) {
            self.slot = match (self.open)() {
                Ok(backend) => Slot::Open(backend),
                Err(error) => Slot::Unavailable(format!("{error:#}")),
            };
        }
        match &mut self.slot {
            Slot::Open(backend) => Ok(backend.as_mut()),
            Slot::Unavailable(reason) => Err(anyhow!("{reason}")),
            Slot::Released => Err(anyhow!(
                "the system clipboard was released when the game quit"
            )),
            Slot::Closed => unreachable!("opened above"),
        }
    }

    /// `clipboard-get` (request `{}`): `{"text":"..."}`, or `{"text":null}` when the clipboard
    /// holds no text. A clipboard that cannot be read is an error, never an old answer.
    pub fn get(&mut self, _request: &str) -> Result<String> {
        let text = match self.backend()?.read_text()? {
            Read::Text(text) => Some(text),
            Read::NoText => None,
        };
        Ok(serde_json::json!({ "text": text }).to_string())
    }

    /// `clipboard-set` (request `{"text":"..."}`): puts the text on the clipboard unchanged and
    /// answers `{}`.
    pub fn set(&mut self, request: &str) -> Result<String> {
        #[derive(serde::Deserialize)]
        struct Request {
            text: String,
        }
        let r: Request = serde_json::from_str(request)?;
        self.backend()?.write_text(&r.text)?;
        Ok("{}".into())
    }

    /// Drops the clipboard for good; true when one was open.
    pub fn release(&mut self) -> bool {
        matches!(
            std::mem::replace(&mut self.slot, Slot::Released),
            Slot::Open(_)
        )
    }
}

static SESSION: Mutex<Session> = Mutex::new(Session::new(open_arboard));

fn session() -> MutexGuard<'static, Session> {
    SESSION
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// The `clipboard-get` control call.
pub fn get(request: &str) -> Result<String> {
    session().get(request)
}

/// The `clipboard-set` control call.
pub fn set(request: &str) -> Result<String> {
    session().set(request)
}

/// Drops the session's clipboard, as arboard requires before the process exits. To be called on
/// the quit path once the game's quit callbacks have run; later calls fail.
pub fn release() -> bool {
    session().release()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::{Arc, Mutex as StdMutex};

    #[test]
    fn incompatible_selection_targets_have_no_text_but_transport_errors_remain_errors() {
        assert_eq!(
            classify_text(Err(arboard::Error::Unknown {
                description: "incorrect type received from clipboard".into()
            }))
            .unwrap(),
            Read::NoText
        );
        assert_eq!(
            classify_text(Err(arboard::Error::ContentNotAvailable)).unwrap(),
            Read::NoText
        );
        assert!(
            classify_text(Err(arboard::Error::Unknown {
                description: "connection lost".into()
            }))
            .is_err()
        );
        assert_eq!(
            classify_text(Ok("Unicode 世界".into())).unwrap(),
            Read::Text("Unicode 世界".into())
        );
    }

    /// What a stand-in clipboard holds and how it was used. Each test owns its own, so the tests
    /// never open arboard or touch the host's clipboard.
    #[derive(Default)]
    struct Shared {
        content: StdMutex<Option<String>>,
        fail_reads: StdMutex<Option<String>>,
        fail_writes: StdMutex<Option<String>>,
        writes: StdMutex<Vec<String>>,
        dropped: AtomicUsize,
    }

    struct Fake(Arc<Shared>);

    impl Backend for Fake {
        fn read_text(&mut self) -> Result<Read> {
            if let Some(reason) = self.0.fail_reads.lock().unwrap().clone() {
                return Err(anyhow!("{reason}"));
            }
            Ok(match self.0.content.lock().unwrap().clone() {
                Some(text) => Read::Text(text),
                None => Read::NoText,
            })
        }

        fn write_text(&mut self, text: &str) -> Result<()> {
            if let Some(reason) = self.0.fail_writes.lock().unwrap().clone() {
                return Err(anyhow!("{reason}"));
            }
            self.0.writes.lock().unwrap().push(text.to_string());
            *self.0.content.lock().unwrap() = Some(text.to_string());
            Ok(())
        }
    }

    impl Drop for Fake {
        fn drop(&mut self) {
            self.0.dropped.fetch_add(1, Ordering::SeqCst);
        }
    }

    /// A session whose opener hands out stand-ins sharing `shared`, counting the opens. `fn`
    /// openers cannot capture, so each test passes its state through its own statics.
    macro_rules! session_with {
        ($shared:ident, $opens:ident) => {{
            static $shared: std::sync::OnceLock<Arc<Shared>> = std::sync::OnceLock::new();
            static $opens: AtomicUsize = AtomicUsize::new(0);
            fn open() -> Result<Box<dyn Backend>> {
                $opens.fetch_add(1, Ordering::SeqCst);
                Ok(Box::new(Fake(Arc::clone(
                    $shared.get_or_init(Default::default),
                ))))
            }
            (
                Session::new(open),
                Arc::clone($shared.get_or_init(Default::default)),
                &$opens,
            )
        }};
    }

    fn text_of(answer: &str) -> Option<String> {
        let json: serde_json::Value = serde_json::from_str(answer).unwrap();
        assert!(json.get("text").is_some(), "no text field in {answer}");
        json["text"].as_str().map(str::to_string)
    }

    #[test]
    fn one_clipboard_is_kept_across_calls() {
        let (mut session, shared, opens) = session_with!(SHARED, OPENS);
        session.set(r#"{"text":"one"}"#).unwrap();
        session.set(r#"{"text":"two"}"#).unwrap();
        assert_eq!(text_of(&session.get("{}").unwrap()).as_deref(), Some("two"));
        assert_eq!(opens.load(Ordering::SeqCst), 1);
        assert_eq!(shared.dropped.load(Ordering::SeqCst), 0);
    }

    #[test]
    fn text_and_line_ends_pass_unchanged() {
        let (mut session, shared, _) = session_with!(SHARED, OPENS);
        let text = "a\nb\r\nc\rd\t\u{e9}\u{1F600}\u{0} ";
        let request = serde_json::json!({ "text": text }).to_string();
        session.set(&request).unwrap();
        assert_eq!(shared.writes.lock().unwrap().as_slice(), [text]);
        assert_eq!(text_of(&session.get("{}").unwrap()).as_deref(), Some(text));
    }

    #[test]
    fn no_text_is_an_answer_not_a_failure() {
        let (mut session, _, _) = session_with!(SHARED, OPENS);
        let answer = session.get("{}").unwrap();
        assert_eq!(answer, r#"{"text":null}"#);
        session.set(r#"{"text":""}"#).unwrap();
        assert_eq!(session.get("{}").unwrap(), r#"{"text":""}"#);
    }

    #[test]
    fn a_failed_read_or_write_is_an_error_and_keeps_the_clipboard() {
        let (mut session, shared, opens) = session_with!(SHARED, OPENS);
        session.set(r#"{"text":"old"}"#).unwrap();
        *shared.fail_reads.lock().unwrap() = Some("occupied".into());
        let error = session.get("{}").unwrap_err();
        assert!(format!("{error:#}").contains("occupied"), "{error:#}");
        *shared.fail_writes.lock().unwrap() = Some("refused".into());
        let error = session.set(r#"{"text":"new"}"#).unwrap_err();
        assert!(format!("{error:#}").contains("refused"), "{error:#}");
        *shared.fail_reads.lock().unwrap() = None;
        *shared.fail_writes.lock().unwrap() = None;
        assert_eq!(text_of(&session.get("{}").unwrap()).as_deref(), Some("old"));
        assert_eq!(opens.load(Ordering::SeqCst), 1);
        assert_eq!(shared.dropped.load(Ordering::SeqCst), 0);
    }

    #[test]
    fn a_clipboard_that_cannot_open_stays_unavailable() {
        static OPENS: AtomicUsize = AtomicUsize::new(0);
        fn open() -> Result<Box<dyn Backend>> {
            OPENS.fetch_add(1, Ordering::SeqCst);
            Err(anyhow!("no display"))
        }
        let mut session = Session::new(open);
        let error = session.get("{}").unwrap_err();
        assert!(format!("{error:#}").contains("no display"), "{error:#}");
        let error = session.set(r#"{"text":"x"}"#).unwrap_err();
        assert!(format!("{error:#}").contains("no display"), "{error:#}");
        assert_eq!(OPENS.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn release_drops_the_clipboard_and_later_calls_fail() {
        let (mut session, shared, opens) = session_with!(SHARED, OPENS);
        session.set(r#"{"text":"kept"}"#).unwrap();
        assert!(session.release());
        assert_eq!(shared.dropped.load(Ordering::SeqCst), 1);
        assert!(session.get("{}").is_err());
        assert!(session.set(r#"{"text":"late"}"#).is_err());
        assert!(!session.release());
        assert_eq!(opens.load(Ordering::SeqCst), 1);
        assert_eq!(shared.writes.lock().unwrap().as_slice(), ["kept"]);
    }

    #[test]
    fn a_set_without_text_is_refused_before_the_clipboard_opens() {
        let (mut session, shared, opens) = session_with!(SHARED, OPENS);
        assert!(session.set("{}").is_err());
        assert!(session.set(r#"{"text":null}"#).is_err());
        assert_eq!(opens.load(Ordering::SeqCst), 0);
        assert!(shared.writes.lock().unwrap().is_empty());
    }

    /// The control calls reach this module through the string-keyed dispatch (read from the
    /// source: `dispatch` needs an initialized runtime).
    #[test]
    fn the_dispatch_routes_both_calls_here() {
        let file = syn::parse_file(include_str!("state.rs")).unwrap();
        let dispatch = file
            .items
            .iter()
            .find_map(|item| match item {
                syn::Item::Fn(f) if f.sig.ident == "dispatch" => Some(f),
                _ => None,
            })
            .expect("state.rs has a dispatch function");
        let arms: Vec<(String, String)> = dispatch
            .block
            .stmts
            .iter()
            .filter_map(|stmt| match stmt {
                syn::Stmt::Expr(syn::Expr::Match(m), _) => Some(m),
                _ => None,
            })
            .flat_map(|m| m.arms.iter())
            .filter_map(|arm| {
                let syn::Pat::Lit(syn::PatLit {
                    lit: syn::Lit::Str(name),
                    ..
                }) = &arm.pat
                else {
                    return None;
                };
                let syn::Expr::Call(call) = arm.body.as_ref() else {
                    return None;
                };
                let syn::Expr::Path(path) = call.func.as_ref() else {
                    return None;
                };
                let path = path
                    .path
                    .segments
                    .iter()
                    .map(|s| s.ident.to_string())
                    .collect::<Vec<_>>()
                    .join("::");
                Some((name.value(), path))
            })
            .collect();
        for (method, function) in [
            ("clipboard-get", "crate::clipboard::get"),
            ("clipboard-set", "crate::clipboard::set"),
        ] {
            assert!(
                arms.iter().any(|(m, f)| m == method && f == function),
                "no `{method}` arm calling {function}"
            );
        }
    }

    /// `Game::shutdown` releases the clipboard once, after the game's quit callbacks (read from
    /// the source: a `Game` needs the managed runtime). Inside the `if !self.stopped` guard, the
    /// release follows the synchronous managed `(self.shutdown)()`, so a copy made in
    /// OnApplicationQuit, `Application.quitting` or `Application.unloading` is still served
    /// until then, and a second shutdown runs neither again.
    #[test]
    fn shutdown_releases_the_clipboard_after_the_quit_callbacks() {
        fn unparen(mut expr: &syn::Expr) -> &syn::Expr {
            while let syn::Expr::Paren(p) = expr {
                expr = p.expr.as_ref();
            }
            expr
        }
        /// `self.<name>`.
        fn is_self_field(expr: &syn::Expr, name: &str) -> bool {
            let syn::Expr::Field(field) = unparen(expr) else {
                return false;
            };
            matches!(&field.member, syn::Member::Named(member) if member == name)
                && matches!(unparen(&field.base), syn::Expr::Path(p) if p.path.is_ident("self"))
        }
        /// What a statement evaluates: `e;`, a tail `e` or `let _ = e;`.
        fn evaluated(stmt: &syn::Stmt) -> Option<&syn::Expr> {
            match stmt {
                syn::Stmt::Expr(expr, _) => Some(expr),
                syn::Stmt::Local(syn::Local {
                    init: Some(init), ..
                }) => Some(init.expr.as_ref()),
                _ => None,
            }
        }
        /// `(self.shutdown)()`, directly or as the only statement of an `unsafe` block.
        fn is_managed_shutdown(expr: &syn::Expr) -> bool {
            match unparen(expr) {
                syn::Expr::Unsafe(block) => matches!(
                    block.block.stmts.as_slice(),
                    [only] if evaluated(only).is_some_and(is_managed_shutdown)
                ),
                syn::Expr::Call(call) => {
                    call.args.is_empty() && is_self_field(&call.func, "shutdown")
                }
                _ => false,
            }
        }
        /// `crate::clipboard::release()`, or the same function by a path game.rs can name it.
        fn is_release(expr: &syn::Expr) -> bool {
            let syn::Expr::Call(call) = unparen(expr) else {
                return false;
            };
            let syn::Expr::Path(path) = unparen(&call.func) else {
                return false;
            };
            let segments = path
                .path
                .segments
                .iter()
                .map(|s| s.ident.to_string())
                .collect::<Vec<_>>();
            call.args.is_empty()
                && path.qself.is_none()
                && path.path.leading_colon.is_none()
                && matches!(
                    segments.as_slice(),
                    [first, clipboard, release]
                        if (first == "crate" || first == "super")
                            && clipboard == "clipboard"
                            && release == "release"
                )
        }

        let file = syn::parse_file(include_str!("game.rs")).unwrap();
        let shutdowns: Vec<&syn::ImplItemFn> = file
            .items
            .iter()
            .filter_map(|item| match item {
                syn::Item::Impl(block)
                    if block.trait_.is_none()
                        && matches!(
                            block.self_ty.as_ref(),
                            syn::Type::Path(ty) if ty.qself.is_none() && ty.path.is_ident("Game")
                        ) =>
                {
                    Some(block)
                }
                _ => None,
            })
            .flat_map(|block| block.items.iter())
            .filter_map(|item| match item {
                syn::ImplItem::Fn(f) if f.sig.ident == "shutdown" => Some(f),
                _ => None,
            })
            .collect();
        assert_eq!(
            shutdowns.len(),
            1,
            "game.rs has one `impl Game` `fn shutdown`"
        );
        let body = &shutdowns[0].block.stmts;
        let guards: Vec<&syn::ExprIf> = body
            .iter()
            .filter_map(|stmt| match evaluated(stmt) {
                Some(syn::Expr::If(guard))
                    if matches!(
                        unparen(&guard.cond),
                        syn::Expr::Unary(syn::ExprUnary {
                            op: syn::UnOp::Not(_),
                            expr,
                            ..
                        }) if is_self_field(expr, "stopped")
                    ) =>
                {
                    Some(guard)
                }
                _ => None,
            })
            .collect();
        assert_eq!(
            guards.len(),
            1,
            "`Game::shutdown` has one `if !self.stopped` guard"
        );
        let guarded = &guards[0].then_branch.stmts;
        let callbacks: Vec<usize> = guarded
            .iter()
            .enumerate()
            .filter(|(_, stmt)| evaluated(stmt).is_some_and(is_managed_shutdown))
            .map(|(i, _)| i)
            .collect();
        assert_eq!(
            callbacks.len(),
            1,
            "the guard calls the managed `(self.shutdown)()` once"
        );
        let releases: Vec<usize> = guarded
            .iter()
            .enumerate()
            .filter(|(_, stmt)| evaluated(stmt).is_some_and(is_release))
            .map(|(i, _)| i)
            .collect();
        assert!(
            releases.len() == 1 && releases[0] > callbacks[0],
            "inside `if !self.stopped`, `crate::clipboard::release()` must run once, after the \
             managed `(self.shutdown)()` (statement {}); found at statements {releases:?}",
            callbacks[0]
        );
        assert!(
            !body
                .iter()
                .any(|stmt| evaluated(stmt).is_some_and(is_release)),
            "`Game::shutdown` releases the clipboard only inside its `if !self.stopped` guard"
        );
    }
}
