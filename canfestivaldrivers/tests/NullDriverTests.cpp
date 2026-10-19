#include "../can_null_win32/can_null_win32.cpp"
#include <chrono>
#include <stdexcept>

static void Require(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

int main()
{
    try
    {
        s_BOARD board = {};
        CAN_HANDLE handle = canOpen_driver(&board);
        Require(handle != NULL, "Null driver did not open");
        Message message = {};
        const auto start = std::chrono::steady_clock::now();
        for (int read = 0; read < 30; read++)
        {
            Require(canReceive_driver(handle, &message) == 0 && message.len == 0,
                "Idle null receive must succeed without fabricating a packet");
        }
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start).count();
        Require(elapsed >= 200, "Null driver busy-spins instead of waiting for idle reads");
        Require(elapsed < 3000, "Null driver idle reads delay shutdown excessively");
        message.cob_id = 0x181;
        message.len = 1;
        Require(canSend_driver(handle, &message) == 0, "Successful null send must use the native zero-success convention");
        Require(canClose_driver(handle) == 1, "Null close failed");
        std::cout << "PASS: real null receive waits, does not fabricate packets, and send reports success\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
