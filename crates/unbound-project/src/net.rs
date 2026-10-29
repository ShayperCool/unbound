//! HTTP to Unity's public services (release index, archives, package registries): one agent
//! whose connects give up quickly, and requests retried a few times, so a lost connection
//! costs seconds rather than the system's connect timeout.
use anyhow::Result;
use std::{sync::OnceLock, time::Duration};

const ATTEMPTS: u32 = 4;

pub fn agent() -> &'static ureq::Agent {
    static AGENT: OnceLock<ureq::Agent> = OnceLock::new();
    AGENT.get_or_init(|| {
        ureq::Agent::config_builder()
            .timeout_connect(Some(Duration::from_secs(15)))
            .timeout_recv_response(Some(Duration::from_secs(60)))
            .build()
            .new_agent()
    })
}

/// `GET url`, read by `read`; transport failures and server errors are retried, client errors
/// (404, ...) are returned at once.
pub fn get<T>(
    url: &str,
    mut read: impl FnMut(ureq::http::Response<ureq::Body>) -> Result<T>,
) -> Result<T> {
    let mut attempt = 1;
    loop {
        let result = agent()
            .get(url)
            .call()
            .map_err(anyhow::Error::from)
            .and_then(&mut read);
        match result {
            Ok(value) => return Ok(value),
            Err(e) if attempt < ATTEMPTS && retryable(&e) => {
                tracing::debug!("GET {url}: {e:#}; retrying");
                std::thread::sleep(Duration::from_secs(1 << (attempt - 1)));
                attempt += 1;
            }
            Err(e) => return Err(e),
        }
    }
}

fn retryable(error: &anyhow::Error) -> bool {
    match error.downcast_ref::<ureq::Error>() {
        Some(ureq::Error::StatusCode(code)) => *code >= 500,
        _ => true,
    }
}

/// Whether `error` is the server's 404.
pub fn is_not_found(error: &anyhow::Error) -> bool {
    matches!(
        error.downcast_ref::<ureq::Error>(),
        Some(ureq::Error::StatusCode(404))
    )
}
