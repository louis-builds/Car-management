# device-simulator

模拟车辆的 C# Console 程序，作为一个 Azure IoT Hub 设备运行。

## 功能
- 每 5 秒模拟一次电池状态（充电时电量上升，空闲时缓慢下降，到 100% 自动停止充电）。
- 只通过 Device Twin 通信，不发遥测消息、不用 Direct Method：
  - 订阅 desired properties（`targetCharging` / `schedule`）作为云端下发的"目标状态"（类似 PLC setpoint）。
  - 状态变化时，或每 30 秒心跳一次，把 reported properties（`batteryLevel` / `isCharging` / `lastUpdated`）写回去，代表设备的"实际状态"（类似传感器 process value）。
- 支持定时充电：desired.schedule 设为 `"HH:mm"`，到点（且当天还没触发过）自动进入充电状态。
- 断网时本地状态机照常按 tick 运行，不依赖云端在线；重连后 reported 自动同步。

## 运行前准备

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download)。
2. 在 Azure Portal 的 IoT Hub 里注册一个设备（Device management > Devices > Add Device），拿到该设备的 **Primary connection string**。
3. 在本目录（`device-simulator/`）下创建一个 `.env` 文件（已加入 `.gitignore`，不会被提交到 git），内容为：

   ```
   IOTHUB_DEVICE_CONNECTION_STRING=HostName=<hub>.azure-devices.net;DeviceId=<deviceId>;SharedAccessKey=<key>
   ```

   程序启动时会自动从当前目录（以及往上找父目录）加载 `.env`，不需要每次手动 `$env:...` 设置环境变量。如果你更喜欢用真正的环境变量，程序也照样支持——`.env` 只是省事的备选方案，二者同时存在时环境变量优先级更高。

## 运行

```bash
dotnet restore
dotnet run
```

启动时会打印一行 `Loaded environment variables from ...\.env`，说明 `.env` 读取成功。

正常启动后会看到类似输出：

```
Car battery simulator started. Press Ctrl+C to stop.
Initial state -> battery: 55.0%, charging: False
[telemetry] {"batteryLevel":54.7,"isCharging":false,"timestamp":"2026-09-11T06:40:00Z"}
```

## 在 Azure Portal 里验证

- **IoT Hub > 设备 > 你的设备 > 设备孪生（Device Twin）**：能看到 reported properties 里的 batteryLevel / isCharging 在更新。
- **IoT Hub > 内置终结点 > 事件中心兼容终结点**，或用 `az iot hub monitor-events` CLI 命令，能看到遥测消息持续进来。
- 之后 Step 3/4 的 Azure Function 会订阅这些消息并写入存储，REST API 再调用这里的 Direct Method 来控制充电。
