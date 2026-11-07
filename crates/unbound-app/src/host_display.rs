//! Host display details that the window abstraction does not retain. Queried only when a
//! monitor description changes, never by a managed Screen getter.
#[derive(Debug, Default)]
pub(crate) struct Details {
    pub id: Option<u64>,
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
        details.id = Some(u64::from(*output));
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

#[cfg(not(target_os = "linux"))]
pub(crate) fn query(
    _name: Option<&str>,
    _position: [i32; 2],
    _size: [u32; 2],
) -> anyhow::Result<Details> {
    anyhow::bail!(
        "physical density, exact refresh ratio and desktop work area are not supplied by this host backend"
    )
}

/// Before the application window exists, use the host's primary monitor as the Editor does.
#[cfg(target_os = "linux")]
pub(crate) fn primary() -> anyhow::Result<unbound_runtime::display::DisplaySnapshot> {
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
        if info.crtc == 0 {
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
            refresh_rate_millihertz: None,
            scale_factor: 1.0,
            modes: Vec::new(),
        });
    }
    anyhow::bail!("the X server reports no active monitor")
}

#[cfg(not(target_os = "linux"))]
pub(crate) fn primary() -> anyhow::Result<unbound_runtime::display::DisplaySnapshot> {
    anyhow::bail!("the primary display is unavailable before this host's window opens")
}
