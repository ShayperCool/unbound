//! The system pointer's position relative to the game window, read from the window system when
//! Bevy has none (before the pointer first moves over the window, and while it is outside it), on
//! macOS: the macOS Editor's Input System reads the system cursor in the Game view's coordinates
//! from its second frame on, moved or not, also outside the view (Unity 6000.0.71f1,
//! tests/projects/initialpointer). The Linux Editor's mouse waits for the pointer to move, as Bevy
//! does, so there nothing is asked. Positions are the window's physical pixels from its top left,
//! not clamped.

/// The window a position is asked for: its AppKit view (macOS).
#[derive(Clone, Copy)]
pub(crate) enum PointerWindow {
    #[cfg(target_os = "macos")]
    AppKit(*mut std::ffi::c_void),
    #[allow(dead_code)]
    None,
}

impl PointerWindow {
    pub(crate) fn of(handle: &bevy::window::RawHandleWrapper) -> Self {
        match handle.get_window_handle() {
            #[cfg(target_os = "macos")]
            wgpu::rwh::RawWindowHandle::AppKit(appkit) => {
                PointerWindow::AppKit(appkit.ns_view.as_ptr())
            }
            _ => PointerWindow::None,
        }
    }
}

/// The pointer in the window's physical pixels from its top left, or None where it is not asked
/// (Linux, no window). Called on the main thread (AppKit).
pub(crate) fn position(window: PointerWindow) -> Option<(f64, f64)> {
    match window {
        #[cfg(target_os = "macos")]
        PointerWindow::AppKit(view) => appkit_position(view),
        PointerWindow::None => None,
    }
}

/// A point in a view's coordinates (points; from the bottom left unless the view is flipped) as
/// physical pixels from the top left.
#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
pub(crate) fn view_point_to_pixels(
    x: f64,
    y: f64,
    view_height: f64,
    flipped: bool,
    scale: f64,
) -> (f64, f64) {
    let from_top = if flipped { y } else { view_height - y };
    (x * scale, from_top * scale)
}

#[cfg(target_os = "macos")]
fn appkit_position(view: *mut std::ffi::c_void) -> Option<(f64, f64)> {
    use objc2::msg_send;
    use objc2::runtime::{AnyClass, AnyObject};
    use objc2_foundation::{NSPoint, NSRect};
    let view = view.cast::<AnyObject>();
    if view.is_null() {
        return None;
    }
    objc2::rc::autoreleasepool(|_| {
        let window: *mut AnyObject = unsafe { msg_send![view, window] };
        if window.is_null() {
            return None;
        }
        // Looked up once: asked every frame while the pointer is outside the window.
        static NS_EVENT: std::sync::OnceLock<Option<&'static AnyClass>> =
            std::sync::OnceLock::new();
        let event_class = (*NS_EVENT.get_or_init(|| AnyClass::get(c"NSEvent")))?;
        let on_screen: NSPoint = unsafe { msg_send![event_class, mouseLocation] };
        let in_window: NSPoint = unsafe { msg_send![window, convertPointFromScreen: on_screen] };
        let none: *mut AnyObject = std::ptr::null_mut();
        let in_view: NSPoint = unsafe { msg_send![view, convertPoint: in_window, fromView: none] };
        let bounds: NSRect = unsafe { msg_send![view, bounds] };
        let flipped: bool = unsafe { msg_send![view, isFlipped] };
        let scale: f64 = unsafe { msg_send![window, backingScaleFactor] };
        Some(view_point_to_pixels(
            in_view.x,
            in_view.y,
            bounds.size.height,
            flipped,
            scale,
        ))
    })
}

#[cfg(test)]
mod tests {
    use super::view_point_to_pixels;

    #[test]
    fn view_points_become_pixels_from_the_top_left() {
        // An unflipped view 360 points high at a 2x backing scale.
        assert_eq!(
            view_point_to_pixels(10.0, 350.0, 360.0, false, 2.0),
            (20.0, 20.0)
        );
        // Outside the view (left of it and above it) the values go below 0, not clamped.
        assert_eq!(
            view_point_to_pixels(-5.0, 370.0, 360.0, false, 1.0),
            (-5.0, -10.0)
        );
        // A flipped view counts from the top already.
        assert_eq!(
            view_point_to_pixels(10.0, 30.0, 360.0, true, 2.0),
            (20.0, 60.0)
        );
    }
}
