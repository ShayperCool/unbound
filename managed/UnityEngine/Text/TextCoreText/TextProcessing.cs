using System;

namespace UnityEngine.TextCore.Text
{
    /// <summary>A character of the text to lay out, after escapes and line-break tags are
    /// resolved: its code point and the source characters it stands for.</summary>
    internal partial struct TextProcessingElement
    {
        public TextProcessingElementType elementType;
        public uint unicode;
        public int stringIndex;
        public int length;
    }

    /// <summary>A rich text tag's attribute: hashes of its name and value, where the value is in
    /// the tag's characters, and its type and unit.</summary>
    internal partial struct RichTextTagAttribute
    {
        public int nameHashCode;
        public int valueHashCode;
        public TagValueType valueType;
        public int valueStartIndex;
        public int valueLength;
        public TagUnitType unitType;
    }

    internal partial struct CharacterSubstitution
    {
        public int index;
        public uint unicode;

        public CharacterSubstitution(int index, uint unicode)
        {
            this.index = index;
            this.unicode = unicode;
        }
    }

    /// <summary>
    /// The value stack of a rich text property (color, size, ...): the bottom item is the default.
    /// A value type over a shared array, so a saved copy restores the depth but not values pushed
    /// over since, as the generator's word-wrap states rely on.
    /// </summary>
    internal partial struct TextProcessingStack<T>
    {
        public T[] itemStack;
        public int index;
        T m_DefaultItem;
        int m_Capacity;
        int m_RolloverSize;
        int m_Count;

        public TextProcessingStack(T[] stack)
        {
            itemStack = stack;
            m_Capacity = stack.Length;
            index = 0;
            m_RolloverSize = 0;
            m_DefaultItem = default;
            m_Count = 0;
        }

        public TextProcessingStack(int capacity) : this(new T[capacity]) { }

        public TextProcessingStack(int capacity, int rolloverSize) : this(new T[capacity]) => m_RolloverSize = rolloverSize;

        public int Count => m_Count;

        public T current => index > 0 ? itemStack[index - 1] : itemStack[0];

        public int rolloverSize
        {
            get => m_RolloverSize;
            set => m_RolloverSize = value;
        }

        internal static void SetDefault(TextProcessingStack<T>[] stack, T item)
        {
            for (int i = 0; i < stack.Length; i++) stack[i].SetDefault(item);
        }

        public void Clear()
        {
            index = 0;
            m_Count = 0;
        }

        public void SetDefault(T item)
        {
            if (itemStack == null)
            {
                m_Capacity = 4;
                itemStack = new T[m_Capacity];
                m_DefaultItem = default;
            }
            itemStack[0] = item;
            index = 1;
            m_Count = 1;
        }

        /// <summary>Pushes a value (dropped when the stack is full).</summary>
        public void Add(T item)
        {
            if (index >= itemStack.Length) return;
            itemStack[index] = item;
            index++;
        }

        /// <summary>Pops a value and returns the one below, never removing the default.</summary>
        public T Remove()
        {
            index--;
            m_Count--;
            if (index <= 0)
            {
                m_Count = 0;
                index = 1;
                return itemStack[0];
            }
            return itemStack[index - 1];
        }

        public void Push(T item)
        {
            if (index == m_Capacity)
            {
                m_Capacity = m_Capacity == 0 ? 4 : m_Capacity * 2;
                Array.Resize(ref itemStack, m_Capacity);
            }
            itemStack[index] = item;
            if (m_RolloverSize == 0)
            {
                index++;
                m_Count++;
            }
            else
            {
                index = (index + 1) % m_RolloverSize;
                m_Count = m_Count < m_RolloverSize ? m_Count + 1 : m_RolloverSize;
            }
        }

        public T Pop()
        {
            if (index == 0 && m_RolloverSize == 0) return default;
            if (m_RolloverSize == 0) index--;
            else
            {
                index = (index - 1) % m_RolloverSize;
                if (index < 0) index += m_RolloverSize;
            }
            var item = itemStack[index];
            itemStack[index] = m_DefaultItem;
            m_Count = m_Count > 0 ? m_Count - 1 : 0;
            return item;
        }

        public T Peek() => index == 0 ? m_DefaultItem : itemStack[index - 1];

        public T CurrentItem() => index > 0 ? itemStack[index - 1] : itemStack[0];

        public T PreviousItem() => index > 1 ? itemStack[index - 2] : itemStack[0];
    }

    /// <summary>How many times each basic style tag is open.</summary>
    internal partial struct FontStyleStack
    {
        public byte bold;
        public byte italic;
        public byte underline;
        public byte strikethrough;
        public byte highlight;
        public byte superscript;
        public byte subscript;
        public byte uppercase;
        public byte lowercase;
        public byte smallcaps;

        public void Clear() => this = default;

        /// <summary>Counts a style tag (small caps are not counted); returns the new count.</summary>
        public byte Add(FontStyles style)
        {
            switch (style)
            {
                case FontStyles.Bold: return ++bold;
                case FontStyles.Italic: return ++italic;
                case FontStyles.Underline: return ++underline;
                case FontStyles.UpperCase: return ++uppercase;
                case FontStyles.LowerCase: return ++lowercase;
                case FontStyles.Strikethrough: return ++strikethrough;
                case FontStyles.Superscript: return ++superscript;
                case FontStyles.Subscript: return ++subscript;
                case FontStyles.Highlight: return ++highlight;
                default: return 0;
            }
        }

        public byte Remove(FontStyles style)
        {
            switch (style)
            {
                case FontStyles.Bold: return bold = Decrement(bold);
                case FontStyles.Italic: return italic = Decrement(italic);
                case FontStyles.Underline: return underline = Decrement(underline);
                case FontStyles.UpperCase: return uppercase = Decrement(uppercase);
                case FontStyles.LowerCase: return lowercase = Decrement(lowercase);
                case FontStyles.Strikethrough: return strikethrough = Decrement(strikethrough);
                case FontStyles.Highlight: return highlight = Decrement(highlight);
                case FontStyles.Superscript: return superscript = Decrement(superscript);
                case FontStyles.Subscript: return subscript = Decrement(subscript);
                default: return 0;
            }
        }

        static byte Decrement(byte count) => count > 1 ? (byte)(count - 1) : (byte)0;
    }

    internal partial struct MaterialReference
    {
        public int index;
        public FontAsset fontAsset;
        public SpriteAsset spriteAsset;
        public Material material;
        public bool isFallbackMaterial;
        public Material fallbackMaterial;
        public float padding;
        public int referenceCount;

        public MaterialReference(int index, FontAsset fontAsset, SpriteAsset spriteAsset, Material material, float padding)
        {
            this.index = index;
            this.fontAsset = fontAsset;
            this.spriteAsset = spriteAsset;
            this.material = material;
            isFallbackMaterial = false;
            fallbackMaterial = null;
            this.padding = padding;
            referenceCount = 0;
        }

        /// <summary>The index of a material in the references (by its hash code), added when new.</summary>
        public static int AddMaterialReference(Material material, FontAsset fontAsset, ref MaterialReference[] materialReferences, System.Collections.Generic.Dictionary<int, int> materialReferenceIndexLookup)
        {
            int key = material.GetHashCode();
            if (materialReferenceIndexLookup.TryGetValue(key, out var index)) return index;
            index = materialReferenceIndexLookup[key] = materialReferenceIndexLookup.Count;
            if (index >= materialReferences.Length) Array.Resize(ref materialReferences, Mathf.NextPowerOfTwo(index + 1));
            materialReferences[index].index = index;
            materialReferences[index].fontAsset = fontAsset;
            materialReferences[index].spriteAsset = null;
            materialReferences[index].material = material;
            materialReferences[index].referenceCount = 0;
            return index;
        }
    }
}
