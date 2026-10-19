#include "../canusb_ascii_parser.h"
#include <cstdio>
#include <iostream>
#include <stdexcept>
#include <vector>

struct Message
{
    unsigned short cob_sender_id;
    unsigned short cob_id;
    unsigned char rtr;
    unsigned char len;
    unsigned char data[8];
};

static void Require(bool condition, const char* description)
{
    if (!condition)
    {
        throw std::runtime_error(description);
    }
}

static std::string Frame(unsigned int sequence)
{
    char text[32];
    std::snprintf(text, sizeof(text), "t%03X2%02X%02X\r",
        sequence % 0x800, sequence % 256, (sequence / 256) % 256);
    return text;
}

static void Drain(CanUsbAsciiParser& parser, std::vector<Message>& messages)
{
    Message message = {};
    while (parser.TryRead(message))
    {
        messages.push_back(message);
    }
}

static void CheckSequence(const std::vector<Message>& messages, unsigned int count)
{
    Require(messages.size() == count, "Burst lost or duplicated frames");
    for (unsigned int index = 0; index < count; ++index)
    {
        const Message& message = messages[index];
        Require(message.cob_id == index % 0x800 && message.len == 2
            && message.data[0] == index % 256 && message.data[1] == (index / 256) % 256
            && message.rtr == 0, "Burst order or payload changed");
    }
}

static void Bursts()
{
    for (unsigned int count : { 3U, 30U, 100U, 10000U })
    {
        std::string stream;
        for (unsigned int index = 0; index < count; ++index)
        {
            stream += Frame(index);
        }

        // Includes one transport read containing far more than the old 500-byte limit.
        for (std::size_t chunk : { std::size_t(1), std::size_t(2), std::size_t(3),
            std::size_t(7), std::size_t(21), std::size_t(22), std::size_t(23),
            std::size_t(64), std::size_t(1024), stream.size() })
        {
            CanUsbAsciiParser parser;
            std::vector<Message> messages;
            for (std::size_t offset = 0; offset < stream.size(); offset += chunk)
            {
                const std::size_t remaining = stream.size() - offset;
                parser.Append(stream.data() + offset, remaining < chunk ? remaining : chunk);
                Drain(parser, messages);
            }

            CheckSequence(messages, count);
            Require(parser.BufferedBytes() == 0, "Completed burst left buffered bytes");
        }
    }
}

static void Fragmentation()
{
    const std::string frame = "t7FF80123456789abcdefA12f\r";
    for (std::size_t split = 0; split <= frame.size(); ++split)
    {
        CanUsbAsciiParser parser;
        Message message = {};
        parser.Append(frame.data(), split);
        const bool complete = parser.TryRead(message);
        Require(complete == (split == frame.size()), "Published an unterminated frame");
        if (!complete)
        {
            parser.Append(frame.data() + split, frame.size() - split);
            Require(parser.TryRead(message), "Fragmented frame was lost");
        }

        Require(message.cob_id == 0x7FF && message.len == 8
            && message.data[0] == 1 && message.data[7] == 0xEF, "Fragment payload changed");
        Require(!parser.TryRead(message), "Fragment emitted twice");
    }

    CanUsbAsciiParser parser;
    std::vector<Message> messages;
    const std::string first = Frame(0) + Frame(1) + "t0022";
    parser.Append(first.data(), first.size());
    Drain(parser, messages);
    Require(parser.BufferedBytes() == 5, "Partial tail was discarded");
    const std::string rest = "0200\r" + Frame(3);
    parser.Append(rest.data(), rest.size());
    Drain(parser, messages);
    CheckSequence(messages, 4);
}

static void Validation()
{
    const std::vector<std::string> invalid = {
        "t1811AG\r", "t1811GA\r", "t18G101\r", "t800101\r",
        "t1819\r", "t181F00112233445566778899AABBCCDDEE\r", "t181X\r",
        "t1811\r", "t18110100\r", "t181101123\r", "t18110112G4\r",
        "T00000181101\r", "r1811\r", "R000001811\r", "F00\r", "z\r", "\a\r",
        std::string("t1811A\0\r", 8)
    };
    for (const std::string& text : invalid)
    {
        CanUsbAsciiParser parser;
        Message message = {};
        message.cob_id = 0x555;
        parser.Append(text.data(), text.size());
        Require(!parser.TryRead(message), "Accepted malformed or unsupported input");
        Require(message.cob_id == 0x555, "Invalid input modified output");
        const std::string recovery = "\r" + Frame(0);
        parser.Append(recovery.data(), recovery.size());
        Require(parser.TryRead(message) && message.cob_id == 0, "Failed to recover after bad input");
        Require(!parser.TryRead(message), "Recovery emitted extra frames");
    }

    for (unsigned int length = 0; length <= 8; ++length)
    {
        CanUsbAsciiParser parser;
        std::string frame = "t000" + std::to_string(length) + std::string(length * 2, 'a') + "\r";
        parser.Append(frame.data(), frame.size());
        Message message = {};
        Require(parser.TryRead(message) && message.len == length, "Rejected valid DLC");
    }
}

static void RecoveryAndReset()
{
    CanUsbAsciiParser parser;
    Message message = {};
    std::string junk(100000, 'x');
    parser.Append(junk.data(), junk.size());
    Require(!parser.TryRead(message) && parser.BufferedBytes() <= 25, "Unbounded incomplete junk");
    junk += "t1811A";
    parser.Append(junk.data(), junk.size());
    Require(!parser.TryRead(message) && parser.BufferedBytes() == 6, "Lost partial frame after junk");
    parser.Append("B\r", 2);
    Require(parser.TryRead(message) && message.data[0] == 0xAB, "Failed to resynchronize");
    const std::string mixed = "\rF00\rz\r" + Frame(0) + "\rz\r" + Frame(1);
    parser.Append(mixed.data(), mixed.size());
    Require(parser.TryRead(message) && message.cob_id == 0, "Status replies hid data");
    const std::string extra = Frame(2);
    parser.Append(extra.data(), extra.size());
    Require(parser.TryRead(message) && message.cob_id == 1, "Append discarded unread frame");
    Require(parser.TryRead(message) && message.cob_id == 2, "Append reordered frames");
    Require(!parser.TryRead(message), "Status reply became a frame");
    const std::string truncated = "t1238AAt181101\r";
    parser.Append(truncated.data(), truncated.size());
    Require(parser.TryRead(message) && message.cob_id == 0x181, "Truncated frame hid following frame");
    parser.Append("t1811A", 6);
    parser.Clear();
    parser.Append("B\r", 2);
    Require(!parser.TryRead(message), "Clear retained previous connection bytes");
}

int main()
{
    try
    {
        Bursts();
        Fragmentation();
        Validation();
        RecoveryAndReset();
        std::cout << "PASS: bursts, fragmentation, validation, recovery and reset\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
