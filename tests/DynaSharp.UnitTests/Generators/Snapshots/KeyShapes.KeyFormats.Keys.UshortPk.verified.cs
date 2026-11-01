
        /// <summary>The values of a parsed <c>ushortpk</c>.</summary>
        /// <param name="UShort">The value of the placeholder <c>UShort</c>.</param>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public readonly record struct UshortPkParts(ushort UShort);

        /// <summary>Builds and parses the partition key <c>ushortpk</c> of <c>KeyFormats</c>, with the template <c>I#{UShort}</c>.</summary>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public static class UshortPk
        {
            private const int BufferLength = 256;

            /// <summary>Formats the complete key.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static string Format(ushort uShort)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, uShort);
                    return __key.ToString();
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Formats the complete key as a key of <c>KeyFormats</c>, which a query of another entity does not take.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> Of(ushort uShort) => new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(Format(uShort), "ushortpk");

            /// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static UshortPkParts Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();

            /// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static UshortPkParts Parse(global::System.ReadOnlySpan<byte> utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();

            /// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>
            public static bool TryParse(string? text, out UshortPkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    if (__key.TryWriteText(text))
                    {
                        return TryParse(__key.WrittenSpan, out parts);
                    }

                    parts = default;
                    return false;
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Tries to parse a key given as UTF-8.</summary>
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out UshortPkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyReader(utf8Text);
                if (__key.TryReadLiteral("I#"u8)
                    && __key.TryReadUInt64("#"u8, out ulong __p0) && __p0 <= ushort.MaxValue
                    && __key.IsAtEnd)
                {
                    parts = new UshortPkParts((ushort)__p0);
                    return true;
                }

                parts = default;
                return false;
            }

            internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer, ushort uShort)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, uShort);
                    __writer.WriteString(__key.WrittenSpan);
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static global::DynaSharp.KeyFormatException Invalid() => new global::DynaSharp.KeyFormatException("The text is not a key of the template I#{UShort}.");

            private static void Encode(ref global::DynaSharp.Keys.KeyWriter __key, ushort uShort)
            {
                __key.WriteLiteral("I#"u8);
                __key.WriteInteger((ulong)uShort, "#"u8, "UShort");
            }
        }
