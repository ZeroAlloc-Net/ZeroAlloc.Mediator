```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8037/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.104
  [Host]     : .NET 10.0.4 (10.0.4, 10.0.426.12010), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.4 (10.0.4, 10.0.426.12010), X64 RyuJIT x86-64-v3


```
| Method                           | Categories   | Mean        | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |------------- |------------:|------:|-------:|----------:|------------:|
| ZeroAllocMediator_Publish_Single | Publish1     |   6.0813 ns |  1.00 |      - |         - |          NA |
| MediatR_Publish_Single           | Publish1     | 243.7506 ns | 40.24 | 0.0629 |     792 B |          NA |
|                                  |              |             |       |        |           |             |
| ZeroAllocMediator_Publish_Multi  | Publish2     |   6.5769 ns |  1.02 |      - |         - |          NA |
| MediatR_Publish_Multi            | Publish2     | 332.3601 ns | 51.73 | 0.0820 |    1032 B |          NA |
|                                  |              |             |       |        |           |             |
| ZeroAllocMediator_Send           | Send         |   0.4857 ns |     ? |      - |         - |           ? |
| MediatR_Send                     | Send         |  78.3486 ns |     ? | 0.0178 |     224 B |           ? |
|                                  |              |             |       |        |           |             |
| ZeroAllocMediator_Send_Static    | SendDI       |   0.6715 ns |     ? |      - |         - |           ? |
| ZeroAllocMediator_Send_DI        | SendDI       |   5.7502 ns |     ? |      - |         - |           ? |
| MediatR_Send_DI                  | SendDI       |  86.3096 ns |     ? | 0.0178 |     224 B |           ? |
|                                  |              |             |       |        |           |             |
| ZeroAllocMediator_SendPipeline   | SendPipeline |   2.8016 ns |  1.27 |      - |         - |          NA |
| MediatR_SendPipeline             | SendPipeline | 101.7776 ns | 46.29 | 0.0120 |     152 B |          NA |
|                                  |              |             |       |        |           |             |
| ZeroAllocMediator_Stream         | Stream       | 202.8163 ns |  1.06 | 0.0081 |     104 B |        1.00 |
| MediatR_Stream                   | Stream       | 654.4371 ns |  3.41 | 0.0420 |     528 B |        5.08 |
