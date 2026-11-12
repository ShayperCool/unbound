//! A scene's systems as the transport, the visibility and the light probes trace them: the
//! surfaces of every system in one hierarchy (a system's rays meet the others' surfaces too)
//! and one numbering of every system's values (clusters, then the cluster tree's parents)
//! followed by the environment's.
use super::clusters::{Clustering, NO_CLUSTER};
use super::geometry::{Bvh, Hit};
use super::transport::{Hierarchy, Refs};
use crate::data::ENVIRONMENT_ID;
use anyhow::{Result, ensure};

pub struct Scene<'a> {
    pub systems: Vec<&'a Clustering>,
    /// Each system's dependency id (its input samples' hash).
    pub hashes: Vec<[u8; 16]>,
    pub(super) bvh: Bvh,
    /// Per triangle of `bvh`: its system and cluster ([`NO_CLUSTER`] for a hidden part).
    owner: Vec<(u32, u32)>,
    /// Where each system's values start in the numbering.
    bases: Vec<u32>,
    pub(super) refs: Refs,
    pub(super) hierarchy: Hierarchy,
}

impl<'a> Scene<'a> {
    pub fn new(systems: Vec<&'a Clustering>, hashes: Vec<[u8; 16]>) -> Result<Self> {
        ensure!(
            !systems.is_empty() && systems.len() == hashes.len(),
            "{} systems with {} hashes",
            systems.len(),
            hashes.len()
        );
        ensure!(
            hashes.iter().all(|h| *h != ENVIRONMENT_ID),
            "a system hash equal to the environment's"
        );
        // Transports number their dependencies in 16 bits (the environment and the systems).
        ensure!(
            systems.len() < u16::MAX as usize,
            "{} systems in one scene",
            systems.len()
        );
        for (i, h) in hashes.iter().enumerate() {
            ensure!(!hashes[..i].contains(h), "two systems of one hash");
        }
        let (mut triangles, mut facing, mut owner) = (Vec::new(), Vec::new(), Vec::new());
        let mut bases = Vec::with_capacity(systems.len());
        let mut base = 0u32;
        for (s, c) in systems.iter().enumerate() {
            bases.push(base);
            base += c.input.values() as u32;
            for t in 0..c.triangle_cluster.len() as u32 {
                triangles.push(c.bvh.triangle(t));
                facing.push(c.bvh.facing(t));
                owner.push((s as u32, c.triangle_cluster[t as usize]));
            }
        }
        let refs = Refs { environment: base };
        let inputs: Vec<&crate::data::InputSamples> = systems.iter().map(|c| &c.input).collect();
        let hierarchy = Hierarchy::new(&inputs, &bases, refs);
        Ok(Scene {
            systems,
            hashes,
            bvh: Bvh::new(triangles, facing),
            owner,
            bases,
            refs,
            hierarchy,
        })
    }

    /// The value a ray's hit adds to: the front of a cluster; nothing for a back face (inside
    /// other geometry) or a part no cluster holds.
    pub(super) fn reference(&self, hit: &Hit) -> Option<u32> {
        let (s, c) = self.owner[hit.triangle as usize];
        (hit.front && c != NO_CLUSTER).then(|| self.bases[s as usize] + c)
    }

    /// The system a value (below [`Refs::environment`]) belongs to, and its index there.
    fn local(&self, r: u32) -> (usize, u32) {
        let s = self.bases.partition_point(|&b| b <= r) - 1;
        (s, r - self.bases[s])
    }

    /// The dependencies of a transport (`own`: its system) or of a light probe set (`None`):
    /// the environment, then the system's own values, then the other systems in order
    /// (Unbound's order: the runtime finds each by its id, and the Editor's lists differ).
    pub(super) fn dependencies(&self, own: Option<usize>) -> Vec<[u8; 16]> {
        let mut out = vec![ENVIRONMENT_ID];
        out.extend(own.map(|o| self.hashes[o]));
        out.extend(
            (0..self.hashes.len())
                .filter(|&s| Some(s) != own)
                .map(|s| self.hashes[s]),
        );
        out
    }

    /// A value as a reference of [`Self::dependencies`]`(own)`: its dependency and index.
    pub(super) fn reference_of(&self, r: u32, own: Option<usize>) -> (u16, u32) {
        if r >= self.refs.environment {
            return (0, r - self.refs.environment);
        }
        let (s, index) = self.local(r);
        let dependency = match own {
            Some(o) if s == o => 1,
            Some(o) => 2 + s - (s > o) as usize,
            None => 1 + s,
        };
        (dependency as u16, index)
    }
}
