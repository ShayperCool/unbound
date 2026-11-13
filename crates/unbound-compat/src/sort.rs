//! The unstable sorts of the C++ standard libraries the Editors of each OS are built with. Where
//! the Editor sorts with an order that leaves some elements equal, the order those elements end in
//! is the algorithm's: the Linux Editor's is GNU libstdc++'s introsort, the macOS Editor's the
//! LLVM libc++ quicksort of the releases before LLVM 15. Both are reproduced step by step, so
//! elements that compare equal end where the Editor of that OS leaves them. Measured with the
//! Unity 6000.0.71f1 Editors: sibling order by name on both OSes (tests/projects/lightmapuvs
//! Names/OrderFbx and Names/Ties); on Linux also the lightmap UV unwrap
//! (tests/projects/lightmapuvs) and the realtime GI system packing, which on macOS take the macOS
//! sort as well (their equal elements, and so libc++'s insertion range for them, are not measured
//! there). Only 6000.0.71f1 is
//! measured; other releases are assumed to sort as it does (a release built with LLVM 15 or
//! later would sort differently on macOS).

use crate::HostOs;

/// One C++ standard library's `std::sort`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EditorSort {
    /// GNU libstdc++: introsort (median of three moved to the front, recursion depth
    /// `2 * floor(log2(n))`, then heap sort), ranges of 16 or fewer left to a final insertion sort.
    LibStdCxx,
    /// LLVM libc++ before LLVM 15: quicksort with a median of three (five from 1000 elements),
    /// sorting networks for up to five elements, insertion sort of ranges up to `insertion`
    /// elements, and an early insertion sort of partitions that needed no swap.
    LibCxxLegacy { insertion: Insertion },
}

/// The range libc++ insertion sorts, which depends on the sorted elements' type.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Insertion {
    /// Up to 6 elements, for elements that are not trivially copyable. The macOS Editor's sibling
    /// order needs it (with 30, tests/projects/lightmapuvs Names/Ties' 28 siblings would be
    /// insertion sorted and keep `K` before `k` as in the file; the Editor gives `k`, `K`).
    UpTo6,
    /// Up to 30 elements, for trivially copyable ones (plain numbers and records). Not measured.
    UpTo30,
}

impl HostOs {
    /// The `std::sort` of this OS's Editor for elements copied as `insertion` says.
    pub fn editor_sort(self, insertion: Insertion) -> EditorSort {
        match self {
            HostOs::Linux => EditorSort::LibStdCxx,
            HostOs::MacOs => EditorSort::LibCxxLegacy { insertion },
        }
    }
}

impl EditorSort {
    /// Sorts `v` by the strict weak order `less` as this library's `std::sort` does.
    pub fn sort_by<T: Copy>(self, v: &mut [T], mut less: impl FnMut(&T, &T) -> bool) {
        match self {
            EditorSort::LibStdCxx => libstdcxx::sort(v, &mut less),
            EditorSort::LibCxxLegacy { insertion } => {
                let limit = match insertion {
                    Insertion::UpTo6 => 6,
                    Insertion::UpTo30 => 30,
                };
                libcxx::sort(v, limit, &mut less)
            }
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

mod libcxx {
    /// From this many elements the pivot is the median of five.
    const NINTHER: usize = 1000;
    /// An early insertion sort gives up after this many moved elements.
    const INCOMPLETE_LIMIT: u32 = 8;

    /// Ranges of up to `limit` elements are insertion sorted.
    pub fn sort<T: Copy>(v: &mut [T], limit: usize, less: &mut impl FnMut(&T, &T) -> bool) {
        sort_range(v, 0, v.len(), limit, less);
    }

    fn sort3<T: Copy>(
        v: &mut [T],
        x: usize,
        y: usize,
        z: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) -> u32 {
        if !less(&v[y], &v[x]) {
            if !less(&v[z], &v[y]) {
                return 0;
            }
            v.swap(y, z);
            if less(&v[y], &v[x]) {
                v.swap(x, y);
                return 2;
            }
            return 1;
        }
        if less(&v[z], &v[y]) {
            v.swap(x, z);
            return 1;
        }
        v.swap(x, y);
        if less(&v[z], &v[y]) {
            v.swap(y, z);
            return 2;
        }
        1
    }

    /// The network of `sort3` followed by sinking each further element into place.
    fn sort_network<T: Copy>(
        v: &mut [T],
        at: &[usize],
        less: &mut impl FnMut(&T, &T) -> bool,
    ) -> u32 {
        let mut swaps = sort3(v, at[0], at[1], at[2], less);
        for k in 3..at.len() {
            for m in (1..=k).rev() {
                if !less(&v[at[m]], &v[at[m - 1]]) {
                    break;
                }
                v.swap(at[m - 1], at[m]);
                swaps += 1;
            }
        }
        swaps
    }

    /// Sorts ranges of up to five elements; false for longer ones.
    fn small<T: Copy>(
        v: &mut [T],
        first: usize,
        last: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) -> bool {
        match last - first {
            0 | 1 => {}
            2 => {
                if less(&v[last - 1], &v[first]) {
                    v.swap(first, last - 1);
                }
            }
            n @ 3..=5 => {
                let at = [first, first + 1, first + 2, first + 3, first + 4];
                sort_network(v, &at[..n], less);
            }
            _ => return false,
        }
        true
    }

    /// Insertion sort after sorting the first three; with `limit`, stops after that many moved
    /// elements and says whether the range is sorted.
    fn insertion_sort_3<T: Copy>(
        v: &mut [T],
        first: usize,
        last: usize,
        limit: Option<u32>,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) -> bool {
        let mut j = first + 2;
        sort3(v, first, first + 1, j, less);
        let mut moved = 0;
        for i in j + 1..last {
            if less(&v[i], &v[j]) {
                let t = v[i];
                let mut k = j;
                j = i;
                loop {
                    v[j] = v[k];
                    j = k;
                    if j == first {
                        break;
                    }
                    k -= 1;
                    if !less(&t, &v[k]) {
                        break;
                    }
                }
                v[j] = t;
                moved += 1;
                if Some(moved) == limit {
                    return i + 1 == last;
                }
            }
            j = i;
        }
        true
    }

    fn insertion_sort_incomplete<T: Copy>(
        v: &mut [T],
        first: usize,
        last: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) -> bool {
        small(v, first, last, less)
            || insertion_sort_3(v, first, last, Some(INCOMPLETE_LIMIT), less)
    }

    fn sort_range<T: Copy>(
        v: &mut [T],
        mut first: usize,
        mut last: usize,
        limit: usize,
        less: &mut impl FnMut(&T, &T) -> bool,
    ) {
        'restart: loop {
            let len = last - first;
            if small(v, first, last, less) {
                return;
            }
            if len <= limit {
                insertion_sort_3(v, first, last, None, less);
                return;
            }
            let lm1 = last - 1;
            let mut m = first + len / 2;
            let mut swaps = if len >= NINTHER {
                let delta = len / 4;
                sort_network(v, &[first, first + delta, m, m + delta, lm1], less)
            } else {
                sort3(v, first, m, lm1, less)
            };
            let mut i = first;
            let mut j = lm1;
            if !less(&v[i], &v[m]) {
                // The first element equals the pivot: find a smaller one from the back as a guard.
                loop {
                    j -= 1;
                    if i == j {
                        // No element is smaller than the pivot: put those equal to the first in front
                        // and sort the rest.
                        i += 1;
                        j = last - 1;
                        if !less(&v[first], &v[j]) {
                            loop {
                                if i == j {
                                    return;
                                }
                                if less(&v[first], &v[i]) {
                                    v.swap(i, j);
                                    i += 1;
                                    break;
                                }
                                i += 1;
                            }
                        }
                        if i == j {
                            return;
                        }
                        loop {
                            while !less(&v[first], &v[i]) {
                                i += 1;
                            }
                            j -= 1;
                            while less(&v[first], &v[j]) {
                                j -= 1;
                            }
                            if i >= j {
                                break;
                            }
                            v.swap(i, j);
                            i += 1;
                        }
                        first = i;
                        continue 'restart;
                    }
                    if less(&v[j], &v[m]) {
                        v.swap(i, j);
                        swaps += 1;
                        break;
                    }
                }
            }
            i += 1;
            if i < j {
                loop {
                    while less(&v[i], &v[m]) {
                        i += 1;
                    }
                    j -= 1;
                    while !less(&v[j], &v[m]) {
                        j -= 1;
                    }
                    if i > j {
                        break;
                    }
                    v.swap(i, j);
                    swaps += 1;
                    if m == i {
                        m = j;
                    }
                    i += 1;
                }
            }
            if i != m && less(&v[m], &v[i]) {
                v.swap(i, m);
                swaps += 1;
            }
            if swaps == 0 {
                let front = insertion_sort_incomplete(v, first, i, less);
                if insertion_sort_incomplete(v, i + 1, last, less) {
                    if front {
                        return;
                    }
                    last = i;
                    continue;
                } else if front {
                    first = i + 1;
                    continue;
                }
            }
            if i - first < last - i {
                sort_range(v, first, i, limit, less);
                first = i + 1;
            } else {
                sort_range(v, i + 1, last, limit, less);
                last = i;
            }
        }
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
    fn both_sorts_order_every_range() {
        for sort in [
            EditorSort::LibStdCxx,
            HostOs::MacOs.editor_sort(Insertion::UpTo6),
            HostOs::MacOs.editor_sort(Insertion::UpTo30),
        ] {
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
    fn equal_elements_end_as_each_library_leaves_them() {
        // Forty elements whose keys repeat (i * 7 mod 3): the order the equal ones end in is
        // each algorithm's own. The expected orders are these implementations' outputs, also
        // given by a separate transcription of both algorithms and checked against the real
        // libc++ 13 `std::sort` for elements that are not trivially copyable; the Editor
        // captures that confirm the algorithms are in unbound-assets' model tests.
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
        #[rustfmt::skip]
        assert_eq!(order(HostOs::MacOs.editor_sort(Insertion::UpTo6)), [
            0, 36, 33, 3, 30, 27, 6, 24, 21, 9, 39, 18, 12, 15, 37, 13, 16, 1, 34, 19, 10, 31, 22, 4,
            7, 25, 28, 5, 26, 29, 23, 8, 32, 2, 11, 35, 17, 14, 38, 20,
        ]);
        // Up to 30 elements of trivially copyable kind are insertion sorted: equal ones keep their
        // order.
        let mut v: Vec<(u64, usize)> = file[..28].to_vec();
        HostOs::MacOs
            .editor_sort(Insertion::UpTo30)
            .sort_by(&mut v, |a, b| a.0 < b.0);
        let mut stable = file[..28].to_vec();
        stable.sort_by_key(|e| e.0);
        assert_eq!(v, stable);
    }
}
