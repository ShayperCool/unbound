//! The unstable sort of the C++ standard library the Linux Editor is built with, GNU libstdc++'s
//! introsort. Where the Editor sorts with an order that leaves some elements equal, the order those
//! elements end in is the algorithm's; it is reproduced step by step, so elements that compare equal
//! end where the Linux Editor leaves them, on every OS (the macOS Editor's libc++ sort is not
//! reproduced). Measured with the Unity 6000.0.71f1 Editor: sibling order by name
//! (tests/projects/lightmapuvs Names/OrderFbx and Names/Ties), the lightmap UV unwrap
//! (tests/projects/lightmapuvs) and the realtime GI system packing. Only 6000.0.71f1 is measured;
//! other releases are assumed to sort as it does.

/// The Editor's `std::sort`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EditorSort {
    /// GNU libstdc++: introsort (median of three moved to the front, recursion depth
    /// `2 * floor(log2(n))`, then heap sort), ranges of 16 or fewer left to a final insertion sort.
    LibStdCxx,
}

impl EditorSort {
    /// Sorts `v` by the strict weak order `less` as this library's `std::sort` does.
    pub fn sort_by<T: Copy>(self, v: &mut [T], mut less: impl FnMut(&T, &T) -> bool) {
        match self {
            EditorSort::LibStdCxx => libstdcxx::sort(v, &mut less),
        }
    }
}

mod libstdcxx {
    /// Ranges of this many elements or fewer are left to the final insertion sort.
    const THRESHOLD: usize = 16;

    pub fn sort<T: Copy>(v: &mut [T], less: &mut impl FnMut(&T, &T) -> bool) {
        let n = v.len();
        if n < 2 {
            return;
        }
        introsort_loop(v, 0, n, 2 * n.ilog2(), less);
        if n > THRESHOLD {
            insertion_sort(v, 0, THRESHOLD, less);
            for i in THRESHOLD..n {
                unguarded_linear_insert(v, i, less);
            }
        } else {
            insertion_sort(v, 0, n, less);
        }
    }

    pub(super) fn introsort_loop<T: Copy>(
        v: &mut [T],
        first: usize,
        mut last: usize,
        mut depth: u32,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) {
        while last - first > THRESHOLD {
            if depth == 0 {
                heap_sort(&mut v[first..last], less);
                return;
            }
            depth -= 1;
            let mid = first + (last - first) / 2;
            move_median_to_first(v, first, first + 1, mid, last - 1, less);
            let cut = unguarded_partition(v, first + 1, last, first, less);
            introsort_loop(v, cut, last, depth, less);
            last = cut;
        }
    }

    fn move_median_to_first<T: Copy>(
        v: &mut [T],
        result: usize,
        a: usize,
        b: usize,
        c: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) {
        let median = if less(&v[a], &v[b]) {
            if less(&v[b], &v[c]) {
                b
            } else if less(&v[a], &v[c]) {
                c
            } else {
                a
            }
        } else if less(&v[a], &v[c]) {
            a
        } else if less(&v[b], &v[c]) {
            c
        } else {
            b
        };
        v.swap(result, median);
    }

    fn unguarded_partition<T: Copy>(
        v: &mut [T],
        mut first: usize,
        mut last: usize,
        pivot: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) -> usize {
        loop {
            while less(&v[first], &v[pivot]) {
                first += 1;
            }
            last -= 1;
            while less(&v[pivot], &v[last]) {
                last -= 1;
            }
            if first >= last {
                return first;
            }
            v.swap(first, last);
            first += 1;
        }
    }

    fn insertion_sort<T: Copy>(
        v: &mut [T],
        first: usize,
        last: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) {
        for i in first + 1..last {
            if less(&v[i], &v[first]) {
                v[first..=i].rotate_right(1);
            } else {
                unguarded_linear_insert(v, i, less);
            }
        }
    }

    fn unguarded_linear_insert<T: Copy>(
        v: &mut [T],
        mut last: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) {
        let value = v[last];
        while less(&value, &v[last - 1]) {
            v[last] = v[last - 1];
            last -= 1;
        }
        v[last] = value;
    }

    /// `std::partial_sort(first, last, last)`: a max-heap of the range, then popped.
    fn heap_sort<T: Copy>(v: &mut [T], less: &mut impl FnMut(&T, &T) -> bool) {
        let len = v.len();
        if len >= 2 {
            let mut parent = (len - 2) / 2;
            loop {
                let value = v[parent];
                adjust_heap(v, parent, len, value, less);
                if parent == 0 {
                    break;
                }
                parent -= 1;
            }
        }
        let mut last = len;
        while last > 1 {
            last -= 1;
            let value = v[last];
            v[last] = v[0];
            adjust_heap(v, 0, last, value, less);
        }
    }

    fn adjust_heap<T: Copy>(
        v: &mut [T],
        mut hole: usize,
        len: usize,
        value: T,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) {
        let top = hole;
        let mut child = hole;
        while child < (len - 1) / 2 {
            child = 2 * (child + 1);
            if less(&v[child], &v[child - 1]) {
                child -= 1;
            }
            v[hole] = v[child];
            hole = child;
        }
        if len % 2 == 0 && child == (len - 2) / 2 {
            child = 2 * (child + 1);
            v[hole] = v[child - 1];
            hole = child - 1;
        }
        let mut parent = hole.wrapping_sub(1) / 2;
        while hole > top && less(&v[parent], &value) {
            v[hole] = v[parent];
            hole = parent;
            parent = hole.wrapping_sub(1) / 2;
        }
        v[hole] = value;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Deterministic pseudo-random keys with many repeats, each with its original index.
    fn keys(n: usize, distinct: u64, seed: u64) -> Vec<(u64, usize)> {
        let mut s = seed;
        (0..n)
            .map(|i| {
                s = s
                    .wrapping_mul(6364136223846793005)
                    .wrapping_add(1442695040888963407);
                ((s >> 33) % distinct, i)
            })
            .collect()
    }

    fn assert_sorted_permutation(v: &[(u64, usize)], what: &str) {
        assert!(v.windows(2).all(|w| w[0].0 <= w[1].0), "{what}");
        let mut idx: Vec<usize> = v.iter().map(|k| k.1).collect();
        idx.sort();
        assert!(idx.iter().copied().eq(0..v.len()), "{what}");
    }

    #[test]
    fn the_sort_orders_every_range() {
        {
            let sort = EditorSort::LibStdCxx;
            for n in [
                0, 1, 2, 3, 4, 5, 6, 7, 16, 17, 31, 100, 999, 1000, 1500, 5000,
            ] {
                for distinct in [1, 2, 7, 1 << 40] {
                    let mut v = keys(n, distinct, n as u64 * 31 + distinct);
                    sort.sort_by(&mut v, |a, b| a.0 < b.0);
                    assert_sorted_permutation(&v, &format!("{sort:?} n {n} distinct {distinct}"));
                }
            }
            // Already ordered and reversed input.
            let mut up: Vec<(u64, usize)> = (0..300).map(|i| (i as u64 / 3, i)).collect();
            sort.sort_by(&mut up, |a, b| a.0 < b.0);
            assert_sorted_permutation(&up, "ascending");
            let mut down: Vec<(u64, usize)> = (0..300).map(|i| (100 - i as u64 / 3, i)).collect();
            sort.sort_by(&mut down, |a, b| a.0 < b.0);
            assert_sorted_permutation(&down, "descending");
        }
    }

    #[test]
    fn libstdcxx_depth_limit_heap_sorts() {
        // At recursion depth zero the introsort hands the range to the heap sort.
        for n in [17, 18, 64, 257] {
            let mut v = keys(n, 5, n as u64);
            libstdcxx::introsort_loop(&mut v, 0, n, 0, &mut |a, b| a.0 < b.0);
            assert_sorted_permutation(&v, &format!("heap n {n}"));
        }
    }

    #[test]
    fn equal_elements_end_as_the_library_leaves_them() {
        // Forty elements whose keys repeat (i * 7 mod 3): the order the equal ones end in is
        // the algorithm's own. The expected order is this implementation's output, also given by
        // a separate transcription of the algorithm; the Editor captures that confirm it are in
        // unbound-assets' model tests.
        let file: Vec<(u64, usize)> = (0..40).map(|i| ((i * 7 % 3) as u64, i)).collect();
        let order = |sort: EditorSort| {
            let mut v = file.clone();
            sort.sort_by(&mut v, |a, b| a.0 < b.0);
            v.iter().map(|e| e.1).collect::<Vec<_>>()
        };
        #[rustfmt::skip]
        assert_eq!(order(EditorSort::LibStdCxx), [
            0, 21, 18, 24, 15, 27, 12, 30, 9, 33, 6, 36, 3, 39, 31, 34, 28, 37, 25, 22, 1, 19, 16, 4,
            13, 7, 10, 2, 20, 38, 5, 35, 8, 32, 11, 29, 14, 26, 17, 23,
        ]);
    }
}
