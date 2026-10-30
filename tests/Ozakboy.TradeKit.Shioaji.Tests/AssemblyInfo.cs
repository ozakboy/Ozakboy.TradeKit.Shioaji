// 這個測試組件不平行執行:監督器測試以行程名稱數「有沒有殘留的假 exe」,假伺服器也各自佔 port,
// 平行跑會讓計數互相干擾。
// The test assembly does not run in parallel: the supervisor tests count leftover fake executables by process name,
// and the fake servers each hold a port, so running in parallel would make the counts interfere.
[assembly: DoNotParallelize]
