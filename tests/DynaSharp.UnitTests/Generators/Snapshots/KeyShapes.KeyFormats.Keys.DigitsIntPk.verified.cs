
        /// <summary>The values of a parsed <c>digitsintpk</c>.</summary>
        /// <param name="Int">The value of the placeholder <c>Int</c>.</param>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public readonly record struct DigitsIntPkParts(int Int);

        /// <summary>Builds and parses the partition key <c>digitsintpk</c> of <c>KeyFormats</c>, with the template <c>D#{Int:d12}</c>.</summary>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public static class DigitsIntPk
        {
            private const int BufferLength = 14;

            /// <summary>Formats the complete key.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static string Format(int @int)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, @int);
                    return __key.ToString();
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Formats the complete key as a key of <c>KeyFormats</c>, which a query of another entity does not take.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> Of(int @int) => new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(Format(@int), "digitsintpk");

            /// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static DigitsIntPkParts Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();

            /// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static DigitsIntPkParts Parse(global::System.ReadOnlySpan<byte> utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();

            /// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>
            public static bool TryParse(string? text, out DigitsIntPkParts parts)
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
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out DigitsIntPkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyReader(utf8Text);
                if (__key.TryReadLiteral("D#"u8)
                    && __key.TryReadDigits(12, out long __p0) && __p0 >= int.MinValue && __p0 <= int.MaxValue
                    && __key.IsAtEnd)
                {
                    parts = new DigitsIntPkParts((int)__p0);
                    return true;
                }

                parts = default;
                return false;
            }

            internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer, int @int)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, @int);
                    __writer.WriteString(__key.WrittenSpan);
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static global::DynaSharp.KeyFormatException Invalid() => new global::DynaSharp.KeyFormatException("The text is not a key of the template D#{Int:d12}.");

            private static void Encode(ref global::DynaSharp.Keys.KeyWriter __key, int @int)
            {
                __key.WriteLiteral("D#"u8);
                __key.WriteDigits((long)@int, 12, "Int");
            }
        }
