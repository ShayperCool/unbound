//! A compact binary encoding of the precomputed data Unbound makes itself (the cache of its own
//! precompute): little-endian numbers, lengths before sequences. Reading checks every length
//! against what is left, so a damaged entry fails instead of allocating or panicking.
use anyhow::{Result, bail, ensure};

pub(crate) struct Writer(pub(crate) Vec<u8>);

pub(crate) struct Reader<'a> {
    bytes: &'a [u8],
    at: usize,
}

impl<'a> Reader<'a> {
    pub(crate) fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, at: 0 }
    }

    fn take(&mut self, n: usize) -> Result<&'a [u8]> {
        ensure!(
            n <= self.bytes.len() - self.at,
            "cached data shorter than its contents"
        );
        let out = &self.bytes[self.at..self.at + n];
        self.at += n;
        Ok(out)
    }

    pub(crate) fn finished(&self) -> Result<()> {
        ensure!(self.at == self.bytes.len(), "cached data longer than its contents");
        Ok(())
    }
}

/// A value of the cache's encoding.
pub(crate) trait Codec: Sized {
    fn write(&self, w: &mut Writer);
    fn read(r: &mut Reader) -> Result<Self>;
    /// The fewest bytes a value takes (to bound a sequence's length by what is left).
    const MIN_BYTES: usize = 1;
}

macro_rules! number {
    ($($t:ty),*) => {$(
        impl Codec for $t {
            fn write(&self, w: &mut Writer) {
                w.0.extend_from_slice(&self.to_le_bytes());
            }
            fn read(r: &mut Reader) -> Result<Self> {
                Ok(<$t>::from_le_bytes(r.take(std::mem::size_of::<$t>())?.try_into().unwrap()))
            }
            const MIN_BYTES: usize = std::mem::size_of::<$t>();
        }
    )*};
}
number!(u8, u16, u32, u64, i64, f32);

impl Codec for usize {
    fn write(&self, w: &mut Writer) {
        (*self as u64).write(w);
    }
    fn read(r: &mut Reader) -> Result<Self> {
        Ok(usize::try_from(u64::read(r)?)?)
    }
    const MIN_BYTES: usize = 8;
}

impl Codec for bool {
    fn write(&self, w: &mut Writer) {
        (*self as u8).write(w);
    }
    fn read(r: &mut Reader) -> Result<Self> {
        match u8::read(r)? {
            0 => Ok(false),
            1 => Ok(true),
            v => bail!("a cached flag of {v}"),
        }
    }
}

impl<T: Codec> Codec for Vec<T> {
    fn write(&self, w: &mut Writer) {
        self.len().write(w);
        for v in self {
            v.write(w);
        }
    }
    fn read(r: &mut Reader) -> Result<Self> {
        let n = usize::read(r)?;
        ensure!(
            n <= (r.bytes.len() - r.at) / T::MIN_BYTES.max(1),
            "a cached sequence of {n} longer than the data"
        );
        (0..n).map(|_| T::read(r)).collect()
    }
    const MIN_BYTES: usize = 8;
}

impl Codec for String {
    fn write(&self, w: &mut Writer) {
        self.as_bytes().to_vec().write(w);
    }
    fn read(r: &mut Reader) -> Result<Self> {
        Ok(String::from_utf8(Vec::<u8>::read(r)?)?)
    }
    const MIN_BYTES: usize = 8;
}

impl<T: Codec, const N: usize> Codec for [T; N] {
    fn write(&self, w: &mut Writer) {
        for v in self {
            v.write(w);
        }
    }
    fn read(r: &mut Reader) -> Result<Self> {
        let v = (0..N).map(|_| T::read(r)).collect::<Result<Vec<T>>>()?;
        Ok(v.try_into().ok().unwrap())
    }
    const MIN_BYTES: usize = N * T::MIN_BYTES;
}

impl<A: Codec, B: Codec> Codec for (A, B) {
    fn write(&self, w: &mut Writer) {
        self.0.write(w);
        self.1.write(w);
    }
    fn read(r: &mut Reader) -> Result<Self> {
        Ok((A::read(r)?, B::read(r)?))
    }
    const MIN_BYTES: usize = A::MIN_BYTES + B::MIN_BYTES;
}

impl<T: Codec> Codec for Option<T> {
    fn write(&self, w: &mut Writer) {
        self.is_some().write(w);
        if let Some(v) = self {
            v.write(w);
        }
    }
    fn read(r: &mut Reader) -> Result<Self> {
        Ok(if bool::read(r)? { Some(T::read(r)?) } else { None })
    }
}

impl<T: Codec> Codec for std::result::Result<T, String> {
    fn write(&self, w: &mut Writer) {
        match self {
            Ok(v) => {
                true.write(w);
                v.write(w);
            }
            Err(e) => {
                false.write(w);
                e.write(w);
            }
        }
    }
    fn read(r: &mut Reader) -> Result<Self> {
        Ok(if bool::read(r)? { Ok(T::read(r)?) } else { Err(String::read(r)?) })
    }
}

/// [`Codec`] for a struct, field by field in the order given.
macro_rules! codec_struct {
    ($t:ty { $($f:ident),* $(,)? }) => {
        impl $crate::codec::Codec for $t {
            fn write(&self, w: &mut $crate::codec::Writer) {
                $( $crate::codec::Codec::write(&self.$f, w); )*
            }
            fn read(r: &mut $crate::codec::Reader) -> anyhow::Result<Self> {
                Ok(Self { $( $f: $crate::codec::Codec::read(r)?, )* })
            }
        }
    };
}
pub(crate) use codec_struct;

impl Codec for std::ops::Range<usize> {
    fn write(&self, w: &mut Writer) {
        self.start.write(w);
        self.end.write(w);
    }
    fn read(r: &mut Reader) -> Result<Self> {
        let (start, end) = (usize::read(r)?, usize::read(r)?);
        ensure!(start <= end, "a cached range {start}..{end}");
        Ok(start..end)
    }
    const MIN_BYTES: usize = 16;
}
