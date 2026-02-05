# PacketGenerator

```
  ____                   _             _      ____                                        _
 |  _ \    __ _    ___  | | __   ___  | |_   / ___|   ___   _ __     ___   _ __    __ _  | |_    ___    _ __
 | |_) |  / _` |  / __| | |/ /  / _ \ | __| | |  _   / _ \ | '_ \   / _ \ | '__|  / _` | | __|  / _ \  | '__|
 |  __/  | (_| | | (__  |   <  |  __/ | |_  | |_| | |  __/ | | | | |  __/ | |    | (_| | | |_  | (_) | | |
 |_|      \__,_|  \___| |_|\_\  \___|  \__|  \____|  \___| |_| |_|  \___| |_|     \__,_|  \__|  \___/  |_|

USAGE:
    PacketGenerator <PROTOCOL> [OPTIONS]

ARGUMENTS:
    <PROTOCOL>    TCP, UDP

OPTIONS:
    -h, --help         Prints help information
    -v, --verbose      Verbose mode
    -i, --interface    Interface name. Required
        --dstIp        The destination IP address. Required
        --dstPort      The destination port. Required
        --srcIp        The source IP address
        --srcPort      The source port
    -c, --count        Packet count to send
```
