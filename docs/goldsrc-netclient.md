# GoldSrc NetClient 协议文档

## 概述

GoldSrc NetClient 是 Half-Life 1 (GoldSrc引擎) 网络协议的客户端实现，参考了 [7244/goldsrc-netclient](https://github.com/7244/goldsrc-netclient) 开源项目。

该协议使用 UDP 进行通信，默认端口为 27015。协议包含连接握手机制、消息加密/解密、Delta 压缩和丢包恢复等特性。

## 项目结构

项目分为三个子项目：
- **GoldsrcNetClient.Core**: 核心库，可发布为 NuGet 包
- **GoldsrcNetClient.Cli**: 命令行客户端
- **GoldsrcNetClient.Test**: 单元测试

## 数据包类型

UDP 数据包使用前 4 字节标识包类型：

| 值 | 类型 | 说明 |
|----|------|------|
| `0xFFFFFFFF` | Connectionless | 无连接数据包（握手阶段） |
| `0xFFFFFFFE` | Split | 分片数据包 |
| 其他 | Connected | 已连接数据包 |

### 已连接数据包头格式

```
[4 bytes] srcSequence | Mode
  - 低30位: 源序列号
  - Bit 30 (0x40000000): 是否分片
  - Bit 31 (0x80000000): 是否为命令
[4 bytes] dstSequence (低30位为确认序列号)
[数据负载, 经过 munge2 异或加密]
```

## 连接流程

参考 [oxiKKK 的文章](https://oxikkk.github.io/articles/goldsrc_connection_process.html)，GoldSrc 的完整连接流程如下：

### 1. 发送挑战请求
```
Client --> Server: \xFF\xFF\xFF\xFFgetchallenge steam\n
```
客户端发送 connectionless 数据包，请求使用 Steam 认证协议。

### 2. 挑战响应（消息类型 'A'）
```
Server --> Client: \xFF\xFF\xFF\xFFA<challenge> <steamGameServerID> <authProto> [<vacSecured>] [<buildNum>]
```
服务器返回 challenge 值（第二个 token）以及 Steam 认证参数。

### 3. 发送连接请求
```
Client --> Server: \xFF\xFF\xFF\xFFconnect 48 <challenge> "\prot\3\unique\-1\raw\steam" "\name\<player>\protocol\48\..."
```
客户端回传 challenge 值，附带 Steam 认证协议信息（prot=3）和 userinfo。

### 4. 连接批准（消息类型 'B'）
```
Server --> Client: \xFF\xFF\xFF\xFFB <userID> "<trueIP>" <unknown> <buildNumber>
```
服务器发送用户 ID 和真实 IP 地址，批准连接。此时 `cls.state = ca_connected`。

### 5. 已连接通信
```
Client --> Server: [Connected] clc_stringcmd "new"
Server --> Client: [Connected] svc_serverinfo, svc_deltadescription...
Client --> Server: [Connected] clc_stringcmd "sendres"
Server --> Client: [Connected] svc_resourcelist, svc_resourcerequest...
Client --> Server: [Connected] clc_fileconsistency, clc_stringcmd "spawn"
Server --> Client: [Connected] 游戏数据流...
```

### 认证协议

| 值 | 协议 | 说明 |
|----|------|------|
| 1 | WON | WON 认证证书 |
| 2 | Hashed CD Key | 使用哈希 CD Key |
| 3 | Steam | Steam 证书（最新版本） |

### Connectionless 消息类型

| 消息 ID | 说明 | 格式 |
|---------|------|------|
| A | 挑战响应 | `A<challenge> <steamID> <authProto> <vacSecured>` |
| B | 连接批准 | `B <userID> "<trueIP>" <unknown> <buildNumber>` |

### 连接数据包格式 (connect packet)

```
\xFF\xFF\xFF\xFFconnect <protocolVersion> <challenge> "<protoInfo>" "<userInfo>"\n
```

其中：
- `protoInfo`: `\prot\<authProto>\unique\-1\raw\steam`
- `userInfo`: `\name\<player>\protocol\48\cl_lc\1\cl_lw\1\rate\20000...`


## 序列号和 ACK

- `srcSequence`: 客户端维护的发送序列号，每次发送递增 1
- `dstSequence`: 最后一个接收到的服务器序列号
- 服务器发送的 `dstSequence` 等于客户端的 `srcSequence - 1` 时表示 ACK
- 丢包检测: 发送数据包后启动 200ms 超时定时器，若未收到 ACK 则重传

## 加密算法 (Munge)

### Munge (Munge1)
用于资源一致性数据的异或加密。

### Munge2
用于已连接数据包负载的异或加密。使用 `mungify_table2` 查找表。

### Munge3
用于 `worldmapCRC` 字段的异或加密。使用 `mungify_table3` 查找表。

### 算法
所有 Munge 变体都使用相同的基本算法：
1. `c ^= ~seq`
2. 对每个字节: `byte ^= (0xA5 | (j << j) | j | table[(i + j) & 0x0F])`
3. `c = byteswap32(c)`
4. `c ^= seq`

解密（UnMunge）执行逆操作。

## 服务端消息 (SVC - Server to Client)

| ID | 名称 | 说明 |
|----|------|------|
| 0x01 | nop | 空操作 |
| 0x02 | disconnect | 断开连接 |
| 0x03 | event | 游戏事件 |
| 0x04 | version | 版本信息 |
| 0x05 | setview | 设置视角实体 |
| 0x06 | sound | 播放声音 |
| 0x07 | time | 设置时间 |
| 0x08 | print | 打印消息到控制台 |
| 0x09 | stufftext | 执行客户端命令文本 |
| 0x0A | setangle | 设置视角角度 |
| 0x0B | serverinfo | 服务器信息 |
| 0x0C | lightstyle | 光源样式 |
| 0x0D | updateuserinfo | 更新用户信息 |
| 0x0E | deltadescription | Delta 压缩类型描述 |
| 0x0F | clientdata | 客户端数据 |
| 0x10 | stopsound | 停止声音 |
| 0x11 | pings | 延迟信息 |
| 0x12 | particle | 粒子效果 |
| 0x13 | damage | 伤害信息 |
| 0x14 | spawnstatic | 生成静态实体 |
| 0x15 | event_reliable | 可靠事件 |
| 0x16 | spawnbaseline | 实体基线数据 |
| 0x17 | temp_entity | 临时实体 |
| 0x18 | setpause | 设置暂停状态 |
| 0x19 | signonnum | 信令编号 |
| 0x1A | centerprint | 居中打印 |
| 0x1D | spawnstaticsound | 生成静态音效 |
| 0x20 | cdtrack | CD 音轨 |
| 0x27 | newusermsg | 注册用户消息 |
| 0x28 | packetentities | 实体数据包 |
| 0x29 | deltapacketentities | Delta 实体数据包 |
| 0x2A | choke | 拥塞控制 |
| 0x2B | resourcelist | 资源列表 |
| 0x2C | newmovevars | 移动变量 |
| 0x2D | resourcerequest | 资源请求 |
| 0x2E | customization | 自定义数据 |
| 0x34 | voiceinit | 语音初始化 |
| 0x35 | voicedata | 语音数据 |
| 0x36 | sendextrainfo | 发送额外信息 |
| 0x39 | sendcvarvalue | 发送 CVar 值 |
| 0x3A | sendcvarvalue2 | 发送 CVar 值2 |
| 0x40+ | User Messages | 用户自定义消息 |

## 服务端信息结构体 (svc_serverinfo)

```c
uint32_t ProtocolVersion;      // 协议版本 (48)
uint32_t SpawnCount;           // 生成计数
uint32_t Munge3_worldmapCRC;   // 加密后的地图CRC
uint8_t  md5_ClientDLL[16];    // 客户端DLL的MD5
uint8_t  MaxClients;           // 最大玩家数
uint8_t  PlayerNumber;         // 本客户端编号
uint8_t  Unknown0;             // 未知
```

## 客户端命令 (CLC - Client to Server)

| ID | 名称 | 说明 |
|----|------|------|
| 0x00 | bad | 无效 |
| 0x01 | nop | 空操作 |
| 0x02 | move | 移动命令 |
| 0x03 | stringcmd | 字符串命令 ("new", "sendres", "spawn") |
| 0x04 | delta | Delta 命令 |
| 0x05 | resourcelist | 资源列表响应 |
| 0x07 | fileconsistency | 文件一致性检查 |
| 0x08 | voicedata | 语音数据 |
| 0x0A | cvarvalue | CVar 值响应 |

## 分片包格式

```
[frag_head_t * MaxFragmentStreams]
[分片数据...]

frag_head_t:
  uint16_t To;             // 总分片数
  uint16_t At;             // 当前分片编号 (从1开始)
  uint16_t StartPosition;  // 起始位置
  uint16_t Size;           // 数据大小
```

## Delta 压缩

GoldSrc 使用 Delta 压缩来高效传输实体状态变更。

### Delta 字段标志

| 标志 | 值 | 说明 |
|------|-----|------|
| BYTE | 1<<0 | 单字节 |
| SHORT | 1<<1 | 双字节 |
| FLOAT | 1<<2 | 浮点数 |
| INTEGER | 1<<3 | 整数 |
| ANGLE | 1<<4 | 角度值 |
| TIMEWINDOW_8 | 1<<5 | 时间窗口 |
| STRING | 1<<7 | 字符串 |
| SIGNED | 1<<31 | 有符号数 |

### 预定义 Delta 类型

- `event_t` - 事件数据
- `weapon_data_t` - 武器数据
- `usercmd_t` - 用户命令
- `custom_entity_state_t` - 自定义实体状态
- `entity_state_player_t` - 玩家实体状态
- `entity_state_t` - 实体状态
- `clientdata_t` - 客户端数据

### Delta 编码格式

```
[3 bits] 有效字节数 (ByteCount)
[ByteCount * 8 bits] 字段掩码位数组 (FDR_MARK)
[变长] 标记为 1 的字段值
```

## 资源一致性 (File Consistency)

服务器发送资源列表后，客户端需要检查本地文件（.wad, .mdl, .spr, .wav 等）的 MD5 并回复一致性数据。

### 资源标志

| 标志 | 值 | 说明 |
|------|-----|------|
| RES_FATALIFMISSING | 1<<0 | 缺失时致命 |
| RES_WASMISSING | 1<<1 | 曾缺失 |
| RES_CUSTOM | 1<<2 | 自定义资源 |
| RES_CHECKFILE | 1<<7 | 检查文件 |

## BZ2 压缩

消息类型 `'B'` 后跟 `BZ2\0` 标志表示后续数据是 BZ2 压缩的。压缩后的消息需要解压后递归处理。

## 网络层

- 使用 UDP Socket
- 非阻塞模式
- 事件驱动 (基于 epoll/select)
- 支持多服务器同时连接

## 参考

- 原项目: [7244/goldsrc-netclient](https://github.com/7244/goldsrc-netclient)
- 协议参考: GoldSrc Engine (Half-Life 1)
- 引擎版本: build 6153
