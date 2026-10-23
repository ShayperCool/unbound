//! The native side of a running game: the API the managed engine calls, the game's preparation and
//! its frame loop. Rendering and windowing live in `unbound-app`.
pub mod audio;
pub mod config;
pub mod game;
pub mod native_api;
pub mod render_protocol;
pub mod state;
pub mod view;
pub mod watch;

pub use config::GameOptions;
pub use game::{FrameInput, Game};
