// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Backport of System.Net.IPNetwork, used before .NET 11: earlier versions mishandle IPv4-mapped IPv6 addresses
// (.NET 8: https://github.com/dotnet/runtime/issues/98427, .NET 9 and 10: https://github.com/dotnet/runtime/issues/131861)
//
// Source: https://github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/runtime/src/libraries/System.Net.Primitives/src/System/Net/IPNetwork.cs

#if !NET11_0_OR_GREATER

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace AspNetCore.IPRestrictions.Endpoints
{
    /// <summary>
    /// Represents an IP network with an <see cref="IPAddress"/> containing the network prefix and an <see cref="int"/> defining the prefix length.
    /// </summary>
    /// <remarks>
    /// <see cref="BaseAddress"/> is always the first usable address of the network.
    /// The constructor and the parsing methods clear any non-zero bits after the network prefix.
    /// </remarks>
    internal readonly struct IPNetwork : IEquatable<IPNetwork>, ISpanFormattable
    {
        private const int IPv4AddressBytes = 4;
        private const int IPv6AddressBytes = 16;

        private readonly IPAddress? _baseAddress;

        /// <summary>
        /// Gets the <see cref="IPAddress"/> that represents the prefix of the network.
        /// </summary>
        public IPAddress BaseAddress => _baseAddress ?? IPAddress.Any;

        /// <summary>
        /// Gets the length of the network prefix in bits.
        /// </summary>
        public int PrefixLength { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IPNetwork"/> class with the specified <see cref="IPAddress"/> and prefix length.
        /// </summary>
        /// <param name="baseAddress">The <see cref="IPAddress"/> that represents the prefix of the network.</param>
        /// <param name="prefixLength">The length of the prefix in bits.</param>
        /// <exception cref="ArgumentNullException">The specified <paramref name="baseAddress"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The specified <paramref name="prefixLength"/> is smaller than `0` or longer than maximum length of <paramref name="prefixLength"/>'s <see cref="AddressFamily"/>.</exception>
        public IPNetwork(IPAddress baseAddress, int prefixLength)
        {
            ArgumentNullException.ThrowIfNull(baseAddress);

            if (prefixLength < 0 || prefixLength > GetMaxPrefixLength(baseAddress))
            {
                ThrowArgumentOutOfRangeException();
            }

            _baseAddress = ClearNonZeroBitsAfterNetworkPrefix(baseAddress, prefixLength);
            PrefixLength = prefixLength;

            [DoesNotReturn]
            static void ThrowArgumentOutOfRangeException() => throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        /// <summary>
        /// Determines whether a given <see cref="IPAddress"/> is part of the network.
        /// </summary>
        /// <param name="address">The <see cref="IPAddress"/> to check.</param>
        /// <returns><see langword="true"/> if the <see cref="IPAddress"/> is part of the network; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException">The specified <paramref name="address"/> is <see langword="null"/>.</exception>
        public bool Contains(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (address.AddressFamily != BaseAddress.AddressFamily && (BaseAddress.AddressFamily != AddressFamily.InterNetwork || !address.IsIPv4MappedToIPv6))
            {
                return false;
            }

            // This prevents comparing zero bytes in the code below.
            if (PrefixLength == 0)
            {
                return true;
            }

            Span<byte> baseAddressBytes = stackalloc byte[IPv6AddressBytes];
            Span<byte> addressBytes = stackalloc byte[IPv6AddressBytes];

            if (BaseAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                // An IPv4 network contains the IPv4-mapped IPv6 form of its addresses
                if (address.IsIPv4MappedToIPv6)
                {
                    address = address.MapToIPv4();
                }

                baseAddressBytes = baseAddressBytes.Slice(0, IPv4AddressBytes);
                addressBytes = addressBytes.Slice(0, IPv4AddressBytes);
            }

            WriteBytes(BaseAddress, baseAddressBytes);
            WriteBytes(address, addressBytes);

            return PrefixEquals(baseAddressBytes, addressBytes, PrefixLength);
        }

        /// <summary>
        /// Converts a CIDR <see cref="string"/> to an <see cref="IPNetwork"/> instance.
        /// </summary>
        /// <param name="s">A <see cref="string"/> that defines an IP network in CIDR notation.</param>
        /// <returns>An <see cref="IPNetwork"/> instance.</returns>
        /// <exception cref="ArgumentNullException">The specified string is <see langword="null"/>.</exception>
        /// <exception cref="FormatException"><paramref name="s"/> is not a valid CIDR network string.</exception>
        public static IPNetwork Parse(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return Parse(s.AsSpan());
        }

        /// <summary>
        /// Converts a CIDR character span to an <see cref="IPNetwork"/> instance.
        /// </summary>
        /// <param name="s">A character span that defines an IP network in CIDR notation.</param>
        /// <returns>An <see cref="IPNetwork"/> instance.</returns>
        /// <exception cref="FormatException"><paramref name="s"/> is not a valid CIDR network string.</exception>
        public static IPNetwork Parse(ReadOnlySpan<char> s)
        {
            if (!TryParse(s, out IPNetwork result))
            {
                throw new FormatException("An invalid IP network was specified.");
            }

            return result;
        }

        /// <summary>
        /// Converts the specified CIDR string to an <see cref="IPNetwork"/> instance and returns a value indicating whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A <see cref="string"/> that defines an IP network in CIDR notation.</param>
        /// <param name="result">When the method returns, contains an <see cref="IPNetwork"/> instance if the conversion succeeds.</param>
        /// <returns><see langword="true"/> if the conversion was succesful; otherwise, <see langword="false"/>.</returns>
        public static bool TryParse(string? s, out IPNetwork result)
        {
            if (s == null)
            {
                result = default;
                return false;
            }

            return TryParse(s.AsSpan(), out result);
        }

        /// <summary>
        /// Converts the specified CIDR character span to an <see cref="IPNetwork"/> instance and returns a value indicating whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A <see cref="string"/> that defines an IP network in CIDR notation.</param>
        /// <param name="result">When the method returns, contains an <see cref="IPNetwork"/> instance if the conversion succeeds.</param>
        /// <returns><see langword="true"/> if the conversion was successful; otherwise, <see langword="false"/>.</returns>
        public static bool TryParse(ReadOnlySpan<char> s, out IPNetwork result)
        {
            int separatorIndex = s.LastIndexOf('/');
            if (separatorIndex >= 0)
            {
                ReadOnlySpan<char> ipAddressSpan = s.Slice(0, separatorIndex);
                ReadOnlySpan<char> prefixLengthSpan = s.Slice(separatorIndex + 1);

                if (IPAddress.TryParse(ipAddressSpan, out IPAddress? address) &&
                    int.TryParse(prefixLengthSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int prefixLength) &&
                    prefixLength <= GetMaxPrefixLength(address))
                {
                    Debug.Assert(prefixLength >= 0); // Parsing with NumberStyles.None should ensure that prefixLength is always non-negative.
                    result = new IPNetwork(address, prefixLength);
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static int GetMaxPrefixLength(IPAddress baseAddress) => baseAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        private static IPAddress ClearNonZeroBitsAfterNetworkPrefix(IPAddress baseAddress, int prefixLength)
        {
            if (prefixLength == 0)
            {
                // Corresponds to 0.0.0.0 or [::]
                return baseAddress.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any;
            }

            Span<byte> bytes = stackalloc byte[IPv6AddressBytes];
            bytes = bytes.Slice(0, WriteBytes(baseAddress, bytes));

            bool changed = false;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte cleared = (byte)(bytes[i] & PrefixMask(prefixLength, i));
                if (cleared != bytes[i])
                {
                    bytes[i] = cleared;
                    changed = true;
                }
            }

            return changed ? new IPAddress(bytes) : baseAddress;
        }

        /// <summary>
        /// Whether the first <paramref name="prefixLength"/> bits of <paramref name="left"/> and <paramref name="right"/> are equal.
        /// </summary>
        private static bool PrefixEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int prefixLength)
        {
            Debug.Assert(left.Length == right.Length);

            for (int i = 0; i < left.Length; i++)
            {
                byte mask = PrefixMask(prefixLength, i);
                if ((left[i] & mask) != (right[i] & mask))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The mask of the network prefix bits within the byte at <paramref name="byteIndex"/>.
        /// </summary>
        private static byte PrefixMask(int prefixLength, int byteIndex)
        {
            int prefixBitsInByte = Math.Clamp(prefixLength - (byteIndex * 8), 0, 8);
            return (byte)(0xFF << (8 - prefixBitsInByte));
        }

        private static int WriteBytes(IPAddress address, Span<byte> destination)
        {
            bool success = address.TryWriteBytes(destination, out int bytesWritten);
            Debug.Assert(success, "The destination is sized for the address family");
            return bytesWritten;
        }

        /// <summary>
        /// Converts the instance to a string containing the <see cref="IPNetwork"/>'s CIDR notation.
        /// </summary>
        /// <returns>The <see cref="string"/> containing the <see cref="IPNetwork"/>'s CIDR notation.</returns>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, stackalloc char[128], $"{BaseAddress}/{(uint)PrefixLength}");

        /// <summary>
        /// Attempts to write the <see cref="IPNetwork"/>'s CIDR notation to the given <paramref name="destination"/> span and returns a value indicating whether the operation succeeded.
        /// </summary>
        /// <param name="destination">The destination span of characters.</param>
        /// <param name="charsWritten">When this method returns, contains the number of characters that were written to <paramref name="destination"/>.</param>
        /// <returns><see langword="true"/> if the formatting was succesful; otherwise <see langword="false"/>.</returns>
        public bool TryFormat(Span<char> destination, out int charsWritten) =>
            destination.TryWrite(CultureInfo.InvariantCulture, $"{BaseAddress}/{(uint)PrefixLength}", out charsWritten);

        /// <summary>
        /// Determines whether two <see cref="IPNetwork"/> instances are equal.
        /// </summary>
        /// <param name="other">The <see cref="IPNetwork"/> instance to compare to this instance.</param>
        /// <returns><see langword="true"/> if the networks are equal; otherwise <see langword="false"/>.</returns>
        public bool Equals(IPNetwork other) =>
            PrefixLength == other.PrefixLength &&
            BaseAddress.Equals(other.BaseAddress);

        /// <summary>
        /// Determines whether two <see cref="IPNetwork"/> instances are equal.
        /// </summary>
        /// <param name="obj">The <see cref="IPNetwork"/> instance to compare to this instance.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an <see cref="IPNetwork"/> instance and the networks are equal; otherwise <see langword="false"/>.</returns>
        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is IPNetwork other &&
            Equals(other);

        /// <summary>
        /// Determines whether the specified instances of <see cref="IPNetwork"/> are equal.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns><see langword="true"/> if the networks are equal; otherwise <see langword="false"/>.</returns>
        public static bool operator ==(IPNetwork left, IPNetwork right) => left.Equals(right);

        /// <summary>
        /// Determines whether the specified instances of <see cref="IPNetwork"/> are not equal.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns><see langword="true"/> if the networks are not equal; otherwise <see langword="false"/>.</returns>
        public static bool operator !=(IPNetwork left, IPNetwork right) => !(left == right);

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>An integer hash value.</returns>
        public override int GetHashCode() => HashCode.Combine(BaseAddress, PrefixLength);

        /// <inheritdoc />
        string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString();

        /// <inheritdoc />
        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            // format and provider are ignored
            TryFormat(destination, out charsWritten);
    }
}

#endif
