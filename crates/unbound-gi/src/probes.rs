//! Light probes lit by precomputed realtime GI: each probe's SphericalHarmonicsL2 coefficients
//! are weighted sums of the input values its probe set references (the systems' cluster and tree
//! values and the environment), as the systems' last updates left them. Matches the Unity
//! 6000.0.71f1 Editor, tests/projects/realtimegi (LightProbes.GetInterpolatedProbe at the probes).
use crate::data::{ABSENT_ID, ENVIRONMENT_ID, ENVIRONMENT_VALUES, ProbeSet, SH_COEFFICIENTS};
use crate::solver::SystemSolver;
use anyhow::{Result, bail};
use std::sync::Arc;

/// A probe's coefficients: red, green, blue, 9 each (as `LightProbes.bakedProbes` stores them).
pub type ProbeSh = [f32; 3 * SH_COEFFICIENTS];

#[derive(Debug, Clone, Copy, PartialEq)]
enum Source {
    Environment,
    System(usize),
    Absent,
}

pub struct ProbeSetSolver {
    set: Arc<ProbeSet>,
    sources: Vec<Source>,
}

impl ProbeSetSolver {
    /// `systems`: the scene's systems; a dependency on a system not among them (another scene's)
    /// fails (not supported yet), as does a reference outside its dependency's values.
    pub fn new(set: Arc<ProbeSet>, systems: &[SystemSolver]) -> Result<Self> {
        let sources = set
            .dependencies
            .iter()
            .map(|id| {
                Ok(if *id == ENVIRONMENT_ID {
                    Source::Environment
                } else if *id == ABSENT_ID {
                    Source::Absent
                } else if let Some(s) = systems.iter().position(|s| s.system().input_hash == *id) {
                    Source::System(s)
                } else {
                    bail!("light probes lit by a precomputed realtime GI system of another scene are not supported yet")
                })
            })
            .collect::<Result<Vec<_>>>()?;
        for probe in &set.probes {
            for &(dependency, index) in &probe.references {
                let limit = match sources.get(dependency as usize) {
                    Some(Source::Environment) => ENVIRONMENT_VALUES,
                    Some(Source::System(s)) => systems[*s].system().input.values(),
                    Some(Source::Absent) => continue,
                    None => bail!("a light probe reference to dependency {dependency}"),
                };
                if index as usize >= limit {
                    bail!("a light probe reference to value {index} of {limit}");
                }
            }
        }
        Ok(Self { set, sources })
    }

    pub fn probe_set(&self) -> &ProbeSet {
        &self.set
    }

    /// Each probe's coefficients from the systems' last updates and the environment's values
    /// ([`ENVIRONMENT_VALUES`] of them), times `DynamicGI.indirectScale` `scale`, into `out` (one
    /// per probe).
    pub fn solve(
        &self,
        systems: &[SystemSolver],
        environment: &[[f32; 3]],
        scale: f32,
        out: &mut [ProbeSh],
    ) {
        for (probe, sh) in self.set.probes.iter().zip(out.iter_mut()) {
            *sh = [0.0; 3 * SH_COEFFICIENTS];
            for (&(dependency, index), weights) in probe.references.iter().zip(&probe.weights) {
                // Dependencies and indices were checked in `new`.
                let value = match self.sources[dependency as usize] {
                    Source::Environment => environment[index as usize],
                    Source::System(s) => systems[s].values()[index as usize],
                    Source::Absent => continue,
                };
                for (c, v) in value.map(|v| v * scale).into_iter().enumerate() {
                    for (k, w) in weights.iter().enumerate() {
                        sh[c * SH_COEFFICIENTS + k] += w * v;
                    }
                }
            }
        }
    }
}
