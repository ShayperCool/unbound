//! Cutting a texel's or a light probe's references down to a budget. The references hang in
//! trees: each system's cluster tree and each face of the environment's levels. A parent stands
//! for its children by their shares of it, so replacing children by their parent changes the
//! light by how far each child's weight is from the parent's weight times that child's share.
//! The joins that change it least go first, over all of the trees at once, until few enough
//! references are left; a tree may be left out whole at the cost of all of its weight. This is
//! the order of joins of the Unity 6000.0.71f1 Editor's precompute: with it Unbound's light
//! probes of tests/projects/realtimegi `Room` keep the Editor's environment references (the
//! same values of its coarser levels for the probes that see much of it), see
//! docs/REALTIME_GI.md, Unbound's precompute.
use super::transport::{Hierarchy, Weight};
use std::cmp::Ordering;
use std::collections::BinaryHeap;

/// How a probe's weight parts count in a join's change: its spherical harmonics coefficients,
/// each band's squared differences over its number of coefficients (1, 3 and 5), so that each
/// band counts alike.
pub(super) const SH_PART_WEIGHTS: [f32; 9] = [
    1.0,
    1.0 / 3.0,
    1.0 / 3.0,
    1.0 / 3.0,
    1.0 / 5.0,
    1.0 / 5.0,
    1.0 / 5.0,
    1.0 / 5.0,
    1.0 / 5.0,
];

/// A texel's weight: one part.
pub(super) const TEXEL_PART_WEIGHTS: [f32; 1] = [1.0];

/// No parent whose first weight part (a texel's or probe's share of its rays) is more than this
/// over the budget takes its children's place, so that no single reference stands for that much
/// of the light.
const LARGEST_JOIN: f32 = 10.0;

const NONE: u32 = u32::MAX;

/// A value of one of the trees the references hang in. A parent of one child is not one: the
/// child stands for it.
struct Node<W> {
    value: u32,
    parent: u32,
    /// The node's children: `children[first..first + count]`.
    first: u32,
    count: u32,
    /// Its share of its parent.
    share: f32,
    weight: W,
    /// What putting it in place of its children changes.
    change: f32,
    /// Whether it is one of the references (at first the leaves).
    in_cut: bool,
}

/// A node whose children are all references: joining them into it is the next step.
#[derive(PartialEq)]
struct Candidate {
    change: f32,
    node: u32,
}

impl Eq for Candidate {}

impl Ord for Candidate {
    /// The smallest change first in a max-heap; equal changes the first node first.
    fn cmp(&self, other: &Self) -> Ordering {
        other
            .change
            .total_cmp(&self.change)
            .then(other.node.cmp(&self.node))
    }
}

impl PartialOrd for Candidate {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

/// Whether a weight takes any light (a reference without any is not counted or kept).
fn takes_light<W: Weight>(w: &W) -> bool {
    w.parts().first().is_some_and(|&p| p != 0.0)
}

/// The change of light from leaving out a weight whole.
fn magnitude(parts: &[f32], part_weights: &[f32]) -> f32 {
    parts
        .iter()
        .zip(part_weights)
        .map(|(&p, &k)| p * p * k)
        .sum()
}

/// Cuts `refs` (sorted by value, each with its weight) to at most `budget` references through
/// the trees of `h`, each weight part's squared change counted by `part_weights`.
pub(super) fn reduce<W: Weight>(
    refs: &mut Vec<(u32, W)>,
    budget: usize,
    h: &Hierarchy,
    part_weights: &[f32],
) {
    if refs.len() <= budget {
        return;
    }
    let mut nodes = trees(refs, h);
    let mut children: Vec<u32> = Vec::with_capacity(nodes.len());
    {
        let mut by_parent: Vec<Vec<u32>> = vec![Vec::new(); nodes.len()];
        for (i, n) in nodes.iter().enumerate() {
            if n.parent != NONE {
                by_parent[n.parent as usize].push(i as u32);
            }
        }
        for (n, list) in nodes.iter_mut().zip(&by_parent) {
            n.first = children.len() as u32;
            n.count = list.len() as u32;
            children.extend_from_slice(list);
        }
    }
    // Parents' weights: the sums of their children's (each child comes after its parent).
    for i in (0..nodes.len()).rev() {
        let p = nodes[i].parent;
        if p != NONE {
            let w = nodes[i].weight;
            nodes[p as usize].weight.join(&w);
        }
    }
    let largest = LARGEST_JOIN / budget as f32;
    for i in 0..nodes.len() {
        let n = &nodes[i];
        if n.count == 0 {
            continue;
        }
        let parent = n.weight.parts();
        let change = if parent.first().is_some_and(|&p| p > largest) {
            f32::MAX
        } else {
            let mut sum = 0.0;
            for &c in &children[n.first as usize..(n.first + n.count) as usize] {
                let child = &nodes[c as usize];
                for ((&cw, &pw), &k) in child.weight.parts().iter().zip(parent).zip(part_weights) {
                    let d = cw - pw * child.share;
                    sum += d * d * k;
                }
            }
            // Per reference the join takes away.
            sum / (n.count - 1).max(1) as f32
        };
        nodes[i].change = change;
    }
    let ready = |nodes: &[Node<W>], i: usize| {
        let n = &nodes[i];
        !n.in_cut
            && n.count > 0
            && children[n.first as usize..(n.first + n.count) as usize]
                .iter()
                .all(|&c| nodes[c as usize].in_cut)
    };
    let mut heap: BinaryHeap<Candidate> = (0..nodes.len())
        .filter(|&i| ready(&nodes, i))
        .map(|i| Candidate {
            change: nodes[i].change,
            node: i as u32,
        })
        .collect();
    let mut count = nodes
        .iter()
        .filter(|n| n.in_cut && takes_light(&n.weight))
        .count();
    while count > budget {
        let Some(Candidate { node, .. }) = heap.pop() else {
            break;
        };
        let i = node as usize;
        if nodes[i].in_cut {
            // A root that already stands for its whole tree: the tree is left out.
            nodes[i].in_cut = false;
            count -= takes_light(&nodes[i].weight) as usize;
            continue;
        }
        nodes[i].in_cut = true;
        count += takes_light(&nodes[i].weight) as usize;
        let (first, n) = (nodes[i].first as usize, nodes[i].count as usize);
        for &c in &children[first..first + n] {
            let c = &mut nodes[c as usize];
            c.in_cut = false;
            count -= takes_light(&c.weight) as usize;
        }
        let parent = nodes[i].parent;
        if parent == NONE {
            let change = magnitude(nodes[i].weight.parts(), part_weights);
            heap.push(Candidate { change, node });
        } else if ready(&nodes, parent as usize) {
            heap.push(Candidate {
                change: nodes[parent as usize].change,
                node: parent,
            });
        }
    }
    refs.clear();
    refs.extend(
        nodes
            .iter()
            .filter(|n| n.in_cut && takes_light(&n.weight))
            .map(|n| (n.value, n.weight)),
    );
    refs.sort_unstable_by_key(|r| r.0);
}

/// Every node of the trees `refs` hang in, each parent before its children (children not
/// linked yet), the leaves with their weights in `refs` and in the cut. A subtree without any of
/// `refs` is one leaf without weight: what joining it changes is the same.
fn trees<W: Weight>(refs: &[(u32, W)], h: &Hierarchy) -> Vec<Node<W>> {
    // The values on the way from each reference to its root.
    let mut above: Vec<u32> = Vec::with_capacity(refs.len() * 8);
    let mut roots: Vec<u32> = Vec::new();
    for &(mut r, _) in refs {
        while let Some(p) = h.parent(r) {
            r = p;
            above.push(r);
        }
        roots.push(r);
    }
    above.sort_unstable();
    above.dedup();
    roots.sort_unstable();
    roots.dedup();
    let mut nodes = Vec::new();
    let mut below = Vec::new();
    let mut stack: Vec<(u32, u32, f32)> = roots.iter().rev().map(|&r| (r, NONE, 1.0)).collect();
    while let Some((mut value, parent, share)) = stack.pop() {
        if above.binary_search(&value).is_ok() {
            h.children(value, &mut below);
            while let [(only, _)] = below[..] {
                value = only;
                h.children(value, &mut below);
            }
        } else {
            below.clear();
        }
        let index = nodes.len() as u32;
        let weight = refs
            .binary_search_by_key(&value, |r| r.0)
            .map_or_else(|_| W::default(), |i| refs[i].1);
        nodes.push(Node {
            value,
            parent,
            first: 0,
            count: 0,
            share,
            weight,
            change: 0.0,
            in_cut: below.is_empty(),
        });
        stack.extend(below.iter().rev().map(|&(c, s)| (c, index, s)));
    }
    nodes
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::InputSamples;
    use crate::precompute::transport::Refs;

    /// A system of `clusters` leaves under the parents of `tree` (per parent its children's
    /// count and their shares in 255ths), numbered as the runtime numbers them.
    fn hierarchy(clusters: usize, children: &[u8], weights: &[u8]) -> Hierarchy {
        let input = InputSamples {
            positions: vec![[0.0; 3]; clusters],
            normals: vec![[0.0, 1.0, 0.0]; clusters],
            cluster_points: (0..=clusters as u32).collect(),
            tree_children: children.to_vec(),
            tree_weights: weights.to_vec(),
        };
        let refs = Refs {
            environment: input.values() as u32,
        };
        Hierarchy::new(&[&input], &[0], refs)
    }

    fn texel(w: f32) -> (f32, [f32; 3]) {
        (w, [0.0; 3])
    }

    #[test]
    fn the_join_that_changes_least_goes_first() {
        // Leaves 0..4; parent 4 over 0 and 1 (halves), parent 5 over 2 and 3 (halves), the root
        // 6 over 4 and 5. Leaves 0 and 1 take their parent's split exactly, 2 and 3 do not.
        let h = hierarchy(4, &[2, 2, 2], &[128, 127, 128, 127, 128, 127]);
        let mut refs = vec![
            (0, texel(0.02)),
            (1, texel(0.02)),
            (2, texel(0.035)),
            (3, texel(0.005)),
        ];
        reduce(&mut refs, 3, &h, &TEXEL_PART_WEIGHTS);
        let values: Vec<u32> = refs.iter().map(|r| r.0).collect();
        assert_eq!(values, vec![2, 3, 4]);
        // The joined reference keeps the light of what it stands for.
        assert!((refs[2].1.0 - 0.04).abs() < 1e-6);
    }

    #[test]
    fn a_parent_taking_too_much_light_joins_last() {
        // The same tree, leaves 0 and 1 now taking most of the light: their parent takes more
        // than the largest share a join may make (10 over a budget of 3), so the other pair,
        // although they part unevenly, are joined first.
        let h = hierarchy(4, &[2, 2, 2], &[128, 127, 128, 127, 128, 127]);
        let mut refs = vec![
            (0, texel(2.0)),
            (1, texel(2.0)),
            (2, texel(0.035)),
            (3, texel(0.005)),
        ];
        reduce(&mut refs, 3, &h, &TEXEL_PART_WEIGHTS);
        let values: Vec<u32> = refs.iter().map(|r| r.0).collect();
        assert_eq!(values, vec![0, 1, 5]);
    }

    #[test]
    fn a_tree_of_little_light_is_left_out_whole() {
        // Two trees: the system's (leaves 0..3 under parent 3) and an environment face whose
        // finest values take almost nothing. Down to one reference, the system's root stands for
        // its tree and the face is left out.
        let h = hierarchy(3, &[3], &[85, 85, 85]);
        let environment = 5;
        let mut refs = vec![
            (0, texel(0.3)),
            (1, texel(0.3)),
            (2, texel(0.3)),
            (environment, texel(0.001)),
            (environment + 1, texel(0.001)),
        ];
        reduce(&mut refs, 1, &h, &TEXEL_PART_WEIGHTS);
        assert_eq!(refs.len(), 1);
        assert_eq!(refs[0].0, 3);
        assert!((refs[0].1.0 - 0.9).abs() < 1e-6);
    }

    #[test]
    fn references_within_the_budget_stay() {
        let h = hierarchy(2, &[2], &[128, 127]);
        let mut refs = vec![(0, texel(0.5)), (1, texel(0.25))];
        reduce(&mut refs, 2, &h, &TEXEL_PART_WEIGHTS);
        assert_eq!(refs.len(), 2);
        // A single child stands for its parent.
        let h = hierarchy(2, &[1, 1, 2], &[255, 255, 128, 127]);
        let mut refs = vec![(0, texel(0.5)), (1, texel(0.25))];
        reduce(&mut refs, 1, &h, &TEXEL_PART_WEIGHTS);
        assert_eq!(refs.iter().map(|r| r.0).collect::<Vec<_>>(), vec![4]);
    }
}
