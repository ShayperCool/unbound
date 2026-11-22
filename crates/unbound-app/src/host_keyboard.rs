//! The X11 keyboard map behind the window's Input System keyboard.
use crate::KeyEvent;
use anyhow::{Context, Result};
use std::{collections::BTreeMap, sync::Mutex};
use x11rb::{
    connection::Connection as _,
    protocol::{Event, xkb, xkb::ConnectionExt as _, xproto::ConnectionExt as _},
    rust_connection::RustConnection,
};

static CONNECTION: Mutex<Option<RustConnection>> = Mutex::new(None);
const CORE_KEYBOARD: xkb::DeviceSpec = 256;

pub(crate) fn initialize() {
    let has_display = |name| std::env::var(name).is_ok_and(|value| !value.is_empty());
    // Match the window backend's display selection. An empty variable does not select a backend.
    if !has_display("DISPLAY") || has_display("WAYLAND_DISPLAY") || has_display("WAYLAND_SOCKET") {
        return;
    }
    match connect() {
        Ok(connection) => *CONNECTION.lock().unwrap() = Some(connection),
        Err(error) => tracing::warn!("X11 keyboard configuration is unavailable: {error:#}"),
    }
}

fn connect() -> Result<RustConnection> {
    let (connection, _) = x11rb::connect(None)?;
    anyhow::ensure!(
        connection.xkb_use_extension(1, 0)?.reply()?.supported,
        "the X server has no XKB extension"
    );
    let mask = xkb::EventType::NEW_KEYBOARD_NOTIFY | xkb::EventType::MAP_NOTIFY;
    let map_parts = xkb::MapPart::KEY_TYPES
        | xkb::MapPart::KEY_SYMS
        | xkb::MapPart::MODIFIER_MAP
        | xkb::MapPart::EXPLICIT_COMPONENTS
        | xkb::MapPart::KEY_ACTIONS
        | xkb::MapPart::KEY_BEHAVIORS
        | xkb::MapPart::VIRTUAL_MODS
        | xkb::MapPart::VIRTUAL_MOD_MAP;
    connection
        .xkb_select_events(
            CORE_KEYBOARD,
            xkb::EventType::default(),
            mask,
            map_parts,
            map_parts,
            &xkb::SelectEventsAux::default(),
        )?
        .check()?;
    refresh(&connection)?;
    Ok(connection)
}

/// Input System configuration notifications are delivered before the next frame's key events.
/// The X11 read is nonblocking. Map queries occur only when a change was received.
pub(crate) fn configuration_events(events: &mut Vec<KeyEvent>) {
    let mut active = CONNECTION.lock().unwrap();
    let Some(connection) = active.as_ref() else {
        return;
    };
    let start = events.len();
    loop {
        match connection.poll_for_event() {
            Ok(Some(
                Event::XkbNewKeyboardNotify(_) | Event::XkbMapNotify(_) | Event::MappingNotify(_),
            )) => {
                events.push(KeyEvent::CONFIGURATION_CHANGED);
            }
            Ok(Some(_)) => continue,
            Ok(None) => break,
            Err(error) => {
                tracing::warn!("X11 keyboard configuration connection ended: {error}");
                *active = None;
                return;
            }
        }
    }
    if events.len() != start
        && let Err(error) = refresh(connection)
    {
        tracing::warn!("reading the updated X11 keyboard map: {error:#}");
    }
}

fn refresh(connection: &RustConnection) -> Result<()> {
    let state = connection.xkb_get_state(CORE_KEYBOARD)?.reply()?;
    let group = u8::from(state.group);
    let names = connection
        .xkb_get_names(
            CORE_KEYBOARD,
            xkb::NameDetail::GROUP_NAMES | xkb::NameDetail::KEY_NAMES,
        )?
        .reply()?;
    let layout = names.value_list.groups.as_ref().and_then(|atoms| {
        let mask = u8::from(names.group_names);
        let group_bit = 1u8.checked_shl(u32::from(group))?;
        (mask & group_bit != 0)
            .then(|| atoms.get((mask & (group_bit - 1)).count_ones() as usize))
            .flatten()
            .copied()
    });
    let layout = layout
        .map(|atom| -> Result<String> {
            let name = connection.get_atom_name(atom)?.reply()?.name;
            Ok(String::from_utf8_lossy(&name).into_owned())
        })
        .transpose()?;

    // Only the key mapping is requested, so the partial-range fields are unused.
    let mapping = connection
        .xkb_get_map(
            CORE_KEYBOARD,
            xkb::MapPart::KEY_SYMS,
            xkb::MapPart::default(),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            xkb::VMod::default(),
            0,
            0,
            0,
            0,
            0,
            0,
        )?
        .reply()?;
    let rows = mapping
        .map
        .syms_rtrn
        .context("XKB did not return the key mapping")?;
    let mut labels = BTreeMap::new();
    for (index, name) in names
        .value_list
        .key_names
        .unwrap_or_default()
        .into_iter()
        .enumerate()
    {
        let Some(key) = input_system_key(name.name) else {
            continue;
        };
        let code = usize::from(names.first_key) + index;
        let Some(row) = code
            .checked_sub(usize::from(mapping.first_key_sym))
            .and_then(|i| rows.get(i))
        else {
            continue;
        };
        let Some(key_value) = unshifted_value(row, usize::from(group)) else {
            continue;
        };
        let Some(character) = xkeysym::Keysym::new(key_value)
            .key_char()
            .filter(|ch| !ch.is_control())
        else {
            continue;
        };
        // Display names use uppercase letters, without expanding one key into multiple characters.
        let mut uppercase = character.to_uppercase();
        let first = uppercase.next().unwrap_or(character);
        labels.insert(
            key,
            if uppercase.next().is_some() {
                character
            } else {
                first
            }
            .to_string(),
        );
    }
    unbound_runtime::keyboard_config::publish(layout, labels);
    Ok(())
}

fn unshifted_value(row: &xkb::KeySymMap, group: usize) -> Option<u32> {
    // The XKB group_info field holds the group count and the out-of-range group rule.
    let count = usize::from(row.group_info & 0x0f);
    if count == 0 {
        return None;
    }
    let effective = if group < count {
        group
    } else {
        match row.group_info & 0xc0 {
            0x40 => count - 1,
            0x80 => {
                let redirected = usize::from((row.group_info & 0x30) >> 4);
                if redirected < count { redirected } else { 0 }
            }
            _ => group % count,
        }
    };
    row.syms.get(effective * usize::from(row.width)).copied()
}

fn input_system_key(name: [u8; 4]) -> Option<u8> {
    // Input System keys identify physical positions. XKB names are independent of the layout.
    let special = match &name {
        b"TLDE" => 4,
        b"AC11" => 5,
        b"AC10" => 6,
        b"AB08" => 7,
        b"AB09" => 8,
        b"AB10" => 9,
        b"BKSL" => 10,
        b"AD11" => 11,
        b"AD12" => 12,
        b"AE11" => 13,
        b"AE12" => 14,
        b"LSGT" => 106,
        _ => 0,
    };
    if special != 0 {
        return Some(special);
    }
    let [_, _, tens, ones] = name;
    if !tens.is_ascii_digit() || !ones.is_ascii_digit() {
        return None;
    }
    let index = usize::from((tens - b'0') * 10 + (ones - b'0')).checked_sub(1)?;
    let row: &[u8] = match &name[..2] {
        b"AD" => b"QWERTYUIOP",
        b"AC" => b"ASDFGHJKL",
        b"AB" => b"ZXCVBNM",
        b"AE" => return (index < 10).then_some(41 + index as u8),
        _ => return None,
    };
    row.get(index).map(|letter| 15 + (letter - b'A'))
}
