//! The native side of a running game: the API the managed engine calls, the game's preparation and
//! its frame loop. Rendering and windowing live in `unbound-app`.
pub mod audio;
pub mod bundles;
pub mod clipboard;
pub mod config;
pub mod cursor;
pub mod display;
pub mod editor_resources;
pub mod fonts;
pub mod game;
pub mod gamepads;
pub mod graphics_device;
pub mod ime;
pub mod lighting;
mod models;
pub mod native_api;
pub mod plugin_api;
pub mod presentation;
pub mod render_commands;
pub mod render_protocol;
pub mod scripted;
pub mod shader_declarations;
pub mod state;
pub mod video;
pub mod view;
pub mod watch;

pub use config::GameOptions;
pub use game::{FrameInput, Game, KeyEvent, restart_in_debug, restart_requested};
pub use scripted::ScriptedInput;
