#ifndef CANUSB_ASCII_PARSER_H
#define CANUSB_ASCII_PARSER_H

#include <cstddef>
#include <string>

// Both transports receive the same CR-terminated CANUSB ASCII stream. A read
// can contain many frames, or only part of one; transport boundaries are not
// packet boundaries. Keep unread bytes until a complete frame is available.
class CanUsbAsciiParser
{
public:
    CanUsbAsciiParser() : m_readOffset(0)
    {
    }

    void Append(const char* bytes, std::size_t count)
    {
        // Compact at read boundaries, not after each frame in a burst.
        if (m_readOffset != 0)
        {
            m_buffer.erase(0, m_readOffset);
            m_readOffset = 0;
        }

        if (count != 0)
        {
            m_buffer.append(bytes, count);
        }
    }

    void Clear()
    {
        m_buffer.clear();
        m_readOffset = 0;
    }

    std::size_t BufferedBytes() const
    {
        return m_buffer.size() - m_readOffset;
    }

    template <typename MessageType>
    bool TryRead(MessageType& message)
    {
        while (m_readOffset < m_buffer.size())
        {
            const std::size_t end = m_buffer.find('\r', m_readOffset);
            if (end == std::string::npos)
            {
                RetainIncompleteFrame();
                return false;
            }

            // A new 't' cannot occur in a valid hexadecimal payload. It is a
            // safe resynchronization point after junk or a truncated frame.
            const std::size_t start = m_buffer.rfind('t', end);
            MessageType decoded = {};
            const bool valid = start != std::string::npos
                && start >= m_readOffset
                && ParseFrame(m_buffer.data() + start, end - start, decoded);

            m_readOffset = end + 1;
            if (m_readOffset == m_buffer.size())
            {
                Clear();
            }

            if (valid)
            {
                message = decoded;
                return true;
            }

            // Command acknowledgements, status replies and malformed lines
            // are not CAN data frames. Continue to any following frame.
        }

        return false;
    }

private:
    enum { MaxFrameTextLength = 25 }; // t + ID + DLC + 8 bytes + optional timestamp.

    static int HexDigit(char value)
    {
        if (value >= '0' && value <= '9')
        {
            return value - '0';
        }

        if (value >= 'a' && value <= 'f')
        {
            return value - 'a' + 10;
        }

        if (value >= 'A' && value <= 'F')
        {
            return value - 'A' + 10;
        }

        return -1;
    }

    template <typename MessageType>
    static bool ParseFrame(const char* text, std::size_t length, MessageType& message)
    {
        if (length < 5 || text[0] != 't')
        {
            return false;
        }

        unsigned short identifier = 0;
        for (std::size_t index = 1; index < 4; ++index)
        {
            const int digit = HexDigit(text[index]);
            if (digit < 0)
            {
                return false;
            }

            identifier = static_cast<unsigned short>((identifier << 4) | digit);
        }

        const int dataLength = HexDigit(text[4]);
        if (identifier > 0x7FF || dataLength < 0 || dataLength > 8)
        {
            return false;
        }

        const std::size_t payloadEnd = 5 + 2 * static_cast<std::size_t>(dataLength);
        if (length != payloadEnd && length != payloadEnd + 4)
        {
            return false;
        }

        for (int index = 0; index < dataLength; ++index)
        {
            const int high = HexDigit(text[5 + 2 * index]);
            const int low = HexDigit(text[6 + 2 * index]);
            if (high < 0 || low < 0)
            {
                return false;
            }

            message.data[index] = static_cast<unsigned char>((high << 4) | low);
        }

        // CANUSB may append a four-digit timestamp. Accept it without changing
        // the existing driver ABI, which has no timestamp field.
        for (std::size_t index = payloadEnd; index < length; ++index)
        {
            if (HexDigit(text[index]) < 0)
            {
                return false;
            }
        }

        message.cob_id = identifier;
        message.len = static_cast<unsigned char>(dataLength);
        message.rtr = 0;
        return true;
    }

    void RetainIncompleteFrame()
    {
        if (BufferedBytes() <= MaxFrameTextLength)
        {
            return;
        }

        // No complete frame can exceed this length. Retain a possible newer
        // partial frame, but do not let a stream of unterminated junk grow.
        const std::size_t start = m_buffer.rfind('t');
        if (start != std::string::npos && start >= m_readOffset
            && m_buffer.size() - start <= MaxFrameTextLength)
        {
            m_buffer.erase(0, start);
            m_readOffset = 0;
        }
        else
        {
            Clear();
        }
    }

    std::string m_buffer;
    std::size_t m_readOffset;
};

#endif
