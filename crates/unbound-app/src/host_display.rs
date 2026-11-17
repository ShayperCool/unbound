//! Host display details that the window abstraction does not retain. Queried only when a
//! monitor description changes, never by a managed Screen getter.
/// The game window's native view, from which the host can tell the window's screen (the
/// AppKit `NSView` on macOS); None before the window exists. Used on the main thread only.
#[derive(Clone, Copy, Default)]
pub(crate) struct WindowView(pub Option<std::ptr::NonNull<std::ffi::c_void>>);

#[derive(Debug, Default)]
pub(crate) struct Details {
    pub refresh_numerator: u32,
    pub refresh_denominator: u32,
    pub dpi: f32,
    pub work_area: Option<[i32; 4]>,
}

#[cfg(target_os = "linux")]
pub(crate) fn query(
    name: Option<&str>,
    position: [i32; 2],
    size: [u32; 2],
) -> anyhow::Result<Details> {
    use x11rb::connection::Connection;
    use x11rb::protocol::{randr::ConnectionExt as _, xproto::ConnectionExt as _};
    let (connection, screen) = x11rb::connect(None)?;
    let root = connection.setup().roots[screen].root;
    let resources = connection
        .randr_get_screen_resources_current(root)?
        .reply()?;
    let mut details = Details {
        refresh_denominator: 1,
        ..Default::default()
    };
    let mut found = false;
    for output in &resources.outputs {
        let info = connection
            .randr_get_output_info(*output, resources.config_timestamp)?
            .reply()?;
        if Some(info.name.as_slice()) != name.map(str::as_bytes) || info.crtc == 0 {
            continue;
        }
        found = true;
        let crtc = connection
            .randr_get_crtc_info(info.crtc, resources.config_timestamp)?
            .reply()?;
        if let Some(mode) = resources.modes.iter().find(|mode| mode.id == crtc.mode) {
            let total = u32::from(mode.htotal) * u32::from(mode.vtotal);
            if total != 0 {
                details.refresh_numerator = mode.dot_clock;
                details.refresh_denominator = total;
            }
        }
        // A display without a physical size cannot supply a density. Unity returns zero.
        if info.mm_width != 0 {
            details.dpi = size[0] as f32 * 25.4 / info.mm_width as f32;
        }
        break;
    }
    if !found {
        anyhow::bail!("the X server has no active output matching the window monitor");
    }
    let atom = |name: &[u8]| -> anyhow::Result<u32> {
        Ok(connection.intern_atom(true, name)?.reply()?.atom)
    };
    let property = |name: &[u8]| -> anyhow::Result<Vec<u32>> {
        let property_atom = atom(name)?;
        if property_atom == 0 {
            return Ok(Vec::new());
        }
        let reply = connection
            .get_property(
                false,
                root,
                property_atom,
                x11rb::protocol::xproto::AtomEnum::CARDINAL,
                0,
                4096,
            )?
            .reply()?;
        Ok(reply
            .value32()
            .map(|values| values.collect())
            .unwrap_or_default())
    };
    let desktop = property(b"_NET_CURRENT_DESKTOP")?
        .first()
        .copied()
        .unwrap_or(0) as usize;
    let areas = property(b"_NET_WORKAREA")?;
    if let Some(area) = areas.get(desktop * 4..desktop * 4 + 4) {
        details.work_area = Some(intersect_work_area(
            position,
            size,
            [
                area[0] as i32,
                area[1] as i32,
                area[2] as i32,
                area[3] as i32,
            ],
        ));
    }
    Ok(details)
}

#[cfg(any(target_os = "linux", test))]
fn intersect_work_area(position: [i32; 2], size: [u32; 2], area: [i32; 4]) -> [i32; 4] {
    let left = i64::from(position[0]).max(i64::from(area[0]));
    let top = i64::from(position[1]).max(i64::from(area[1]));
    let right =
        (i64::from(position[0]) + i64::from(size[0])).min(i64::from(area[0]) + i64::from(area[2]));
    let bottom =
        (i64::from(position[1]) + i64::from(size[1])).min(i64::from(area[1]) + i64::from(area[3]));
    [
        (left - i64::from(position[0])) as i32,
        (top - i64::from(position[1])) as i32,
        (right - left).max(0) as i32,
        (bottom - top).max(0) as i32,
    ]
}

#[cfg(test)]
mod tests {
    use super::intersect_work_area;
    #[test]
    fn work_area_uses_monitor_local_coordinates_with_panels() {
        assert_eq!(
            intersect_work_area([0, 0], [1920, 1080], [0, 32, 1920, 960]),
            [0, 32, 1920, 960]
        );
        assert_eq!(
            intersect_work_area([-1920, 0], [1920, 1080], [-1920, 24, 3840, 1056]),
            [0, 24, 1920, 1056]
        );
        assert_eq!(
            intersect_work_area([1920, 0], [1920, 1080], [0, 0, 3840, 1080]),
            [0, 0, 1920, 1080]
        );
    }
}

/// Read the selected output, or the primary output before the application window exists.
#[cfg(target_os = "linux")]
pub(crate) fn snapshot(
    name: Option<&str>,
    _window: WindowView,
) -> anyhow::Result<unbound_runtime::display::DisplaySnapshot> {
    use x11rb::connection::Connection;
    use x11rb::protocol::randr::ConnectionExt as _;
    let (connection, screen) = x11rb::connect(None)?;
    let root = connection.setup().roots[screen].root;
    let resources = connection
        .randr_get_screen_resources_current(root)?
        .reply()?;
    let primary = connection.randr_get_output_primary(root)?.reply()?.output;
    for output in resources
        .outputs
        .iter()
        .filter(|output| **output == primary)
        .chain(
            resources
                .outputs
                .iter()
                .filter(|output| **output != primary),
        )
    {
        let info = connection
            .randr_get_output_info(*output, resources.config_timestamp)?
            .reply()?;
        if info.crtc == 0 || name.is_some_and(|name| info.name != name.as_bytes()) {
            continue;
        }
        let crtc = connection
            .randr_get_crtc_info(info.crtc, resources.config_timestamp)?
            .reply()?;
        let name = String::from_utf8_lossy(&info.name).into_owned();
        let width = u32::from(crtc.width);
        let height = u32::from(crtc.height);
        let details = query(
            Some(&name),
            [i32::from(crtc.x), i32::from(crtc.y)],
            [width, height],
        )?;
        return Ok(unbound_runtime::display::DisplaySnapshot {
            id: u64::from(*output),
            name: Some(name),
            width,
            height,
            dpi: details.dpi,
            info_refresh_numerator: details.refresh_numerator,
            info_refresh_denominator: details.refresh_denominator,
            work_area: details
                .work_area
                .unwrap_or([0, 0, width as i32, height as i32]),
            refresh_rate_millihertz: rate_millihertz(
                details.refresh_numerator,
                details.refresh_denominator,
            ),
            refresh_ratio: None,
            scale_factor: 1.0,
            modes: info
                .modes
                .iter()
                .filter_map(|id| resources.modes.iter().find(|mode| mode.id == *id))
                .map(|mode| unbound_runtime::display::DisplayMode {
                    width: u32::from(mode.width),
                    height: u32::from(mode.height),
                    refresh_rate_millihertz: rate_millihertz(
                        mode.dot_clock,
                        u32::from(mode.htotal) * u32::from(mode.vtotal),
                    )
                    .unwrap_or(0),
                    refresh_ratio: None,
                })
                .collect(),
        });
    }
    anyhow::bail!("the X server reports no active monitor")
}

/// The display the window is on, as the macOS Editor reports it (matches the Unity 6000.0.71f1
/// macOS Editor, tests/projects/macdisplay): the desktop's usable area (the screen without the
/// menu bar and Dock, `NSScreen.visibleFrame`) in pixels as the current resolution, at the
/// display link's nominal refresh period as an exact ratio; one resolution per display mode's
/// pixel size at that rate, the current mode's as the usable area, plus the desktop's size in
/// points without a rate; the density of the current mode's pixels over the screen's width.
/// Reads AppKit: called on the main thread.
#[cfg(target_os = "macos")]
pub(crate) fn snapshot(
    name: Option<&str>,
    window: WindowView,
) -> anyhow::Result<unbound_runtime::display::DisplaySnapshot> {
    use objc2::msg_send;
    use objc2::rc::{Retained, autoreleasepool};
    use objc2::runtime::{AnyClass, AnyObject};
    use objc2_foundation::{NSRect, NSString};
    use std::ffi::c_void;
    use unbound_runtime::display::{DisplayMode, DisplaySnapshot};

    #[repr(C)]
    struct CGSize {
        width: f64,
        height: f64,
    }
    #[repr(C)]
    struct CVTime {
        value: i64,
        scale: i32,
        flags: i32,
    }
    #[link(name = "CoreGraphics", kind = "framework")]
    unsafe extern "C" {
        fn CGDisplayPixelsWide(display: u32) -> usize;
        fn CGDisplayPixelsHigh(display: u32) -> usize;
        fn CGDisplayCopyDisplayMode(display: u32) -> *mut c_void;
        fn CGDisplayCopyAllDisplayModes(display: u32, options: *const c_void) -> *mut c_void;
        fn CGDisplayModeGetPixelWidth(mode: *const c_void) -> usize;
        fn CGDisplayModeGetPixelHeight(mode: *const c_void) -> usize;
        fn CGDisplayModeGetRefreshRate(mode: *const c_void) -> f64;
        fn CGDisplayModeRelease(mode: *mut c_void);
        fn CGDisplayScreenSize(display: u32) -> CGSize;
    }
    #[link(name = "CoreFoundation", kind = "framework")]
    unsafe extern "C" {
        fn CFArrayGetCount(array: *const c_void) -> isize;
        fn CFArrayGetValueAtIndex(array: *const c_void, index: isize) -> *const c_void;
        fn CFRelease(object: *const c_void);
    }
    #[link(name = "CoreVideo", kind = "framework")]
    unsafe extern "C" {
        fn CVDisplayLinkCreateWithCGDisplay(display: u32, link: *mut *mut c_void) -> i32;
        fn CVDisplayLinkGetNominalOutputVideoRefreshPeriod(link: *mut c_void) -> CVTime;
        fn CVDisplayLinkRelease(link: *mut c_void);
    }

    autoreleasepool(|_| {
        let class =
            AnyClass::get(c"NSScreen").ok_or_else(|| anyhow::anyhow!("AppKit has no NSScreen"))?;
        let key = NSString::from_str("NSScreenNumber");
        let number_of = |screen: *mut AnyObject| -> Option<u32> {
            let description: *mut AnyObject = unsafe { msg_send![screen, deviceDescription] };
            let number: *mut AnyObject = unsafe { msg_send![description, objectForKey: &*key] };
            (!number.is_null()).then(|| unsafe { msg_send![number, unsignedIntValue] })
        };
        let screens: *mut AnyObject = unsafe { msg_send![class, screens] };
        let count: usize = if screens.is_null() {
            0
        } else {
            unsafe { msg_send![screens, count] }
        };
        // The window's own screen when the window is known; else the screen of the monitor's
        // name; else the main screen.
        let mut screen: *mut AnyObject = match window.0 {
            Some(view) => {
                let ns_window: *mut AnyObject =
                    unsafe { msg_send![view.as_ptr().cast::<AnyObject>(), window] };
                if ns_window.is_null() {
                    std::ptr::null_mut()
                } else {
                    unsafe { msg_send![ns_window, screen] }
                }
            }
            None => std::ptr::null_mut(),
        };
        let mut screen_name = None;
        if !screen.is_null() {
            let localized: Option<Retained<NSString>> = unsafe { msg_send![screen, localizedName] };
            screen_name = localized.map(|n| n.to_string());
        }
        for index in 0..if screen.is_null() { count } else { 0 } {
            let candidate: *mut AnyObject = unsafe { msg_send![screens, objectAtIndex: index] };
            let localized: Option<Retained<NSString>> =
                unsafe { msg_send![candidate, localizedName] };
            let localized = localized.map(|n| n.to_string());
            if name.is_none() || localized.as_deref() == name {
                screen = candidate;
                screen_name = localized;
                break;
            }
        }
        if screen.is_null() {
            screen = unsafe { msg_send![class, mainScreen] };
            if screen.is_null() {
                anyhow::bail!("AppKit reports no screen");
            }
            let localized: Option<Retained<NSString>> = unsafe { msg_send![screen, localizedName] };
            screen_name = localized.map(|n| n.to_string());
        }
        let frame: NSRect = unsafe { msg_send![screen, frame] };
        let visible: NSRect = unsafe { msg_send![screen, visibleFrame] };
        let scale: f64 = unsafe { msg_send![screen, backingScaleFactor] };
        let Some(display) = number_of(screen) else {
            anyhow::bail!("the screen has no display number");
        };
        let pixels = |points: f64| (points * scale).round() as i64;
        let width = pixels(visible.size.width) as u32;
        let height = pixels(visible.size.height) as u32;
        // Cocoa's origin: the desktop's bottom-left corner.
        let work_area = [
            pixels(visible.origin.x - frame.origin.x) as i32,
            pixels(visible.origin.y - frame.origin.y) as i32,
            width as i32,
            height as i32,
        ];
        let mut link = std::ptr::null_mut();
        let period = (unsafe { CVDisplayLinkCreateWithCGDisplay(display, &mut link) } == 0
            && !link.is_null())
        .then(|| {
            let time = unsafe { CVDisplayLinkGetNominalOutputVideoRefreshPeriod(link) };
            unsafe { CVDisplayLinkRelease(link) };
            time
        })
        .filter(|time| time.value > 0 && time.scale > 0 && time.value <= i64::from(u32::MAX));
        let current = unsafe { CGDisplayCopyDisplayMode(display) };
        if current.is_null() {
            anyhow::bail!("CoreGraphics reports no mode for display {display}");
        }
        let current_size = unsafe {
            (
                CGDisplayModeGetPixelWidth(current),
                CGDisplayModeGetPixelHeight(current),
            )
        };
        let current_rate = unsafe { CGDisplayModeGetRefreshRate(current) };
        unsafe { CGDisplayModeRelease(current) };
        let millihertz = (current_rate * 1000.0).round() as u32;
        let ratio = period.map(|time| (time.scale as u32, time.value as u32));
        let (info_numerator, info_denominator) = ratio.unwrap_or((millihertz, 1000));
        let mut modes = vec![DisplayMode {
            width: unsafe { CGDisplayPixelsWide(display) } as u32,
            height: unsafe { CGDisplayPixelsHigh(display) } as u32,
            refresh_rate_millihertz: 0,
            refresh_ratio: Some((0, 1)),
        }];
        let all = unsafe { CGDisplayCopyAllDisplayModes(display, std::ptr::null()) };
        if !all.is_null() {
            for index in 0..unsafe { CFArrayGetCount(all) } {
                let mode = unsafe { CFArrayGetValueAtIndex(all, index) };
                let size = unsafe {
                    (
                        CGDisplayModeGetPixelWidth(mode),
                        CGDisplayModeGetPixelHeight(mode),
                    )
                };
                let (mode_width, mode_height) = if size == current_size {
                    (width, height)
                } else {
                    (size.0 as u32, size.1 as u32)
                };
                modes.push(DisplayMode {
                    width: mode_width,
                    height: mode_height,
                    refresh_rate_millihertz: millihertz,
                    refresh_ratio: ratio,
                });
            }
            unsafe { CFRelease(all) };
        }
        let millimetres = unsafe { CGDisplayScreenSize(display) };
        // A display without a physical size cannot supply a density: zero, as on Linux.
        let dpi = if millimetres.width > 0.0 {
            (current_size.0 as f64 * 25.4 / millimetres.width) as f32
        } else {
            0.0
        };
        Ok(DisplaySnapshot {
            id: u64::from(display),
            dpi,
            info_refresh_numerator: info_numerator,
            info_refresh_denominator: info_denominator,
            work_area,
            name: screen_name,
            width,
            height,
            refresh_rate_millihertz: Some(millihertz),
            refresh_ratio: ratio,
            scale_factor: scale,
            modes,
        })
    })
}

/// The largest content a titled, resizable window can have on the main screen, in pixels: the
/// screen's usable area (`NSScreen.visibleFrame`) less the window's frame (its title bar), at
/// the screen's backing scale. AppKit keeps a window within it. None where unknown. Reads AppKit:
/// called on the main thread, before the window opens.
#[cfg(target_os = "macos")]
pub(crate) fn largest_window_content() -> Option<(i32, i32)> {
    use objc2::msg_send;
    use objc2::runtime::{AnyClass, AnyObject};
    use objc2_foundation::NSRect;
    // NSWindowStyleMask: titled, closable, miniaturizable, resizable (the game window's).
    const STYLE: usize = 1 | 2 | 4 | 8;
    objc2::rc::autoreleasepool(|_| {
        let screen_class = AnyClass::get(c"NSScreen")?;
        let window_class = AnyClass::get(c"NSWindow")?;
        let screen: *mut AnyObject = unsafe { msg_send![screen_class, mainScreen] };
        if screen.is_null() {
            return None;
        }
        let visible: NSRect = unsafe { msg_send![screen, visibleFrame] };
        let scale: f64 = unsafe { msg_send![screen, backingScaleFactor] };
        let content: NSRect =
            unsafe { msg_send![window_class, contentRectForFrameRect: visible, styleMask: STYLE] };
        Some((
            (content.size.width * scale).floor() as i32,
            (content.size.height * scale).floor() as i32,
        ))
    })
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn largest_window_content() -> Option<(i32, i32)> {
    None
}

#[cfg(not(any(target_os = "linux", target_os = "macos")))]
pub(crate) fn snapshot(
    _name: Option<&str>,
    _window: WindowView,
) -> anyhow::Result<unbound_runtime::display::DisplaySnapshot> {
    anyhow::bail!(
        "physical density, exact refresh ratio and desktop work area are not supplied by this host backend"
    )
}

#[cfg(target_os = "linux")]
fn rate_millihertz(numerator: u32, denominator: u32) -> Option<u32> {
    (numerator != 0 && denominator != 0)
        .then(|| ((u64::from(numerator) * 1000) / u64::from(denominator)) as u32)
}

static DIRTY: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// Checks only memory in the frame loop. The blocking event connection wakes on changes.
pub(crate) fn changed() -> bool {
    DIRTY.swap(false, std::sync::atomic::Ordering::AcqRel)
}

pub(crate) fn watch() {
    #[cfg(target_os = "linux")]
    {
        static START: std::sync::Once = std::sync::Once::new();
        START.call_once(|| {
            if let Err(error) = std::thread::Builder::new()
                .name("unbound-displays".into())
                .spawn(|| {
                    if let Err(error) = watch_events() {
                        tracing::warn!(
                            "Screen display change notifications unavailable: {error:#}"
                        );
                    }
                })
            {
                tracing::warn!("Screen display change notifications unavailable: {error}");
            }
        });
    }
}

#[cfg(target_os = "linux")]
fn watch_events() -> anyhow::Result<()> {
    use std::sync::atomic::Ordering;
    use x11rb::{
        connection::Connection,
        protocol::{
            randr::{ConnectionExt as _, NotifyMask},
            xproto::{ChangeWindowAttributesAux, ConnectionExt as _, EventMask},
        },
    };
    let (connection, screen) = x11rb::connect(None)?;
    let root = connection.setup().roots[screen].root;
    // Create the atoms even without a window manager: a desktop may begin publishing them later.
    let work_area = connection
        .intern_atom(false, b"_NET_WORKAREA")?
        .reply()?
        .atom;
    let desktop = connection
        .intern_atom(false, b"_NET_CURRENT_DESKTOP")?
        .reply()?
        .atom;
    connection
        .change_window_attributes(
            root,
            &ChangeWindowAttributesAux::new().event_mask(EventMask::PROPERTY_CHANGE),
        )?
        .check()?;
    connection
        .randr_select_input(
            root,
            NotifyMask::SCREEN_CHANGE
                | NotifyMask::CRTC_CHANGE
                | NotifyMask::OUTPUT_CHANGE
                | NotifyMask::OUTPUT_PROPERTY,
        )?
        .check()?;
    connection.flush()?;
    DIRTY.store(true, Ordering::Release);
    loop {
        let event = connection.wait_for_event()?;
        let changed = is_display_event(&event, root, work_area, desktop);
        if changed {
            DIRTY.store(true, Ordering::Release);
        }
    }
}

#[cfg(target_os = "linux")]
fn is_display_event(
    event: &x11rb::protocol::Event,
    root: u32,
    work_area: u32,
    desktop: u32,
) -> bool {
    use x11rb::protocol::Event;
    match event {
        Event::RandrScreenChangeNotify(_) | Event::RandrNotify(_) => true,
        Event::PropertyNotify(event) => {
            event.window == root && (event.atom == work_area || event.atom == desktop)
        }
        _ => false,
    }
}

#[cfg(all(test, target_os = "linux"))]
mod change_tests {
    use super::is_display_event;
    use x11rb::protocol::{Event, randr::ScreenChangeNotifyEvent, xproto::PropertyNotifyEvent};
    #[test]
    fn screen_changes_refresh_even_when_the_output_identity_is_unchanged() {
        let mut event = ScreenChangeNotifyEvent::default();
        event.width = 1920;
        event.height = 1080;
        assert!(is_display_event(
            &Event::RandrScreenChangeNotify(event),
            1,
            2,
            3
        ));
        event.width = 2560;
        event.height = 1440;
        assert!(is_display_event(
            &Event::RandrScreenChangeNotify(event),
            1,
            2,
            3
        ));
        let mut property = PropertyNotifyEvent::default();
        property.window = 1;
        property.atom = 2;
        assert!(is_display_event(&Event::PropertyNotify(property), 1, 2, 3));
        property.atom = 4;
        assert!(!is_display_event(&Event::PropertyNotify(property), 1, 2, 3));
    }
}
