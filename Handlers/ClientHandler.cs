using H2PControl.Events;
using H2PControl.Models;
using H2PControl.Sockets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace H2PControl.Handlers
{
    public class ClientHandler : ChatEventBase
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        //추가
        private readonly bool _isServer;
        private readonly Dictionary<string, (FileStream fs, long received, long total)> _fileReceiveTracker = 
                new Dictionary<string, (FileStream fs, long received, long total)>();

        public override event EventHandler<ChatEventArgs> Connected;
        public override event EventHandler<ChatEventArgs> Disconnected;
        public override event EventHandler<ChatEventArgs> Received;

        public ClientHandler(TcpClient client, bool isServer = false)
        {
            _client = client;
            _stream = client.GetStream();
            _isServer = isServer;
        }


        public ChatHub InitialData { get; private set; }

       
        // 1).메세지보냄(S,C 모두 해당)
        public void Send(ChatHub hub)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(hub.ToJsonString());
                byte[] lengthBuffer = BitConverter.GetBytes(buffer.Length);

                if (_stream == null || !_stream.CanWrite)
                    return;

                _stream.Write(lengthBuffer, 0, lengthBuffer.Length);
                _stream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"클라이언트로 메세지 전송 중 오류 발생: {ex.Message}");
            }

            // ChatHub 보내면(_stream.Write) 받는쪽 (_stream.ReadAsync)로 이동한다.
        }

        // 2).메세지받음(S,C 모두 해당)
        public async Task HandleClientAsync()
        {
            //메세지(크기) 바이트 보내고 다음에 메세지 전송
            byte[] sizeBuffer = new byte[4];
            int read;

            try
            {
                while (true)
                {
                    read = await _stream.ReadAsync(sizeBuffer, 0, sizeBuffer.Length);
                    if (read == 0)
                        break;

                    int size = BitConverter.ToInt32(sizeBuffer, 0);
                    byte[] buffer = new byte[size];

                    int offset = 0;

                    while (offset < size)
                    {
                        int r = await _stream.ReadAsync(buffer, offset, size - offset);
                        if (r == 0) break;
                        offset += r;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, size);
                    var hub = ChatHub.Parse(message);

                    await HandleIncomingHubAsync(hub);

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"클라이언트 요청 처리 중 오류 발생: {ex.Message}");
            }
            finally
            {
                _client.Close();
                Disconnected?.Invoke(this, new ChatEventArgs(this, InitialData));
            }
        }

        // 3).ChatState 값에 따라 분기 (S,C 모두해당)
        // 4).분기 후 파일을 전송하거나 Received?로 메세지 전달(S,C 모두 해당)
        private async Task HandleIncomingHubAsync(ChatHub hub)
        {
            switch (hub.State)
            {
                case ChatState.Initial:
                case ChatState.Connect:
                    InitialData = hub;
                    Debug.Print($"{(_isServer ? "서버" : "클라이언트")} 연결 이벤트 발생");
                    Connected?.Invoke(this, new ChatEventArgs(this, hub));
                    break;

                case ChatState.Message:
                case ChatState.진행상황:
                    Debug.Print($"{(_isServer ? "서버" : "클라이언트")} 메시지 수신");
                    Received?.Invoke(this, new ChatEventArgs(this, hub));
                    break;

                case ChatState.FileStart:
                    await Handle파일수신시작(hub);
                    break;

                case ChatState.FileChunk:
                    await Handle파일수신체크(hub);
                    break;

                case ChatState.FileEnd:
                    await Handle파일수신완료(hub);
                    break;

                case ChatState.Ack:
                    Console.WriteLine($"[응답] {hub.Message}");
                    break;
                    
            }
        }

        // ============================================================
        // 파일 수신 시작
        // ============================================================
        private async Task Handle파일수신시작(ChatHub hub)
        {
            string[] 플레그분리 = hub.플레그.Split('|');
            string 실제플레그 = 플레그분리[0];
            string saveDir = "";
            //플레그 값에 따라 저장할 위치를 정한다.
            switch (실제플레그)
            {
                case "S To C : LIST파일전송":         // main컴(서버)에서 sub1 ~ sub3까지 파일을 분할해서 전송한다. 
                    saveDir = $"{저장.기본입력폴더}"; // 저장.기본입력폴더 = @"d:\Easy약관Plus\List\"
                    break;
                case "C To S : 개별PDF전송":          // sub1~sub3에서 PDF파일 완성 후 main으로 전송한다. (.END : 상태값으로 전송완료 의미)

                    int 재시도 = 0;
                    while (재시도 < 60)
                    {
                        //서버쪽에서 재시작을 하면 서버쪽 작업을 기다려 주지않고 바로 전송부터 하기 때문에 서버쪽에 정보관련 설정을 하기도 전에 수신받아 
                        //저장부터 시작한다. 재시작 하면 모든값이 초기화 되기 때문에 출력폴더도 초기화 된다.
                        if (string.IsNullOrEmpty(저장.출력폴더))
                        {
                            try
                            {
                                DirectoryInfo dir = new DirectoryInfo($@"d:\Easy약관Plus\Output\감시\");
                                FileInfo[] 단계02파일들 = dir.GetFiles("*.단계02");

                                if (단계02파일들.Length > 0)
                                {
                                    using (var fss = new FileStream(단계02파일들[0].FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                    using (var sr = new StreamReader(fss, Encoding.GetEncoding(949)))
                                    {
                                        string 내용 = sr.ReadToEnd();
                                        string[] 분리 = 내용.Split('|');
                                        if (분리.Length == 3)
                                        {
                                            저장.출력폴더 = 분리[2].Trim();
                                            if (!저장.출력폴더.EndsWith("\\")) 저장.출력폴더 += "\\";

                                            break; // 복구 성공! 60초 루프 즉시 탈출
                                        }
                                    }
                                }
                            }
                            catch (Exception) {
                            }
                        } else {
                            break;
                        }

                        재시도++;
                        await Task.Delay(1000); // 1초 대기 후 다시 확인
                    }

                    if (string.IsNullOrEmpty(저장.출력폴더))
                    {
                        saveDir = @"d:\Easy약관Plus\Output\TmpFile\";
                    }
                    else
                    {
                        if (Path.GetExtension(hub.FileName).ToUpper() == ".END")
                            saveDir = $"{저장.출력폴더}StateFile\\";
                        else
                            saveDir = $"{저장.출력폴더}Y\\";
                    }

                    break;
            }

            Directory.CreateDirectory(saveDir);

            string filePath = Path.Combine(saveDir, hub.FileName); 
            var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            
            _fileReceiveTracker[hub.FileName] = (fs, 0L, hub.FileSize);

            // ✅ 파일 수신 시작 이벤트를 UI로 알림
            Received?.Invoke(this, new ChatEventArgs(this, new ChatHub
            {
                RoomId = hub.RoomId,
                FileName = hub.FileName,
                UserName = hub.UserName,
                Message = $"[수신시작] {hub.UserName} : {hub.FileName} ({hub.FileSize / 1024 / 1024.0:F1}MB)"
            }));

            await Task.CompletedTask;
        }

        // ============================================================
        // 파일 수신 중
        // ============================================================
        private async Task Handle파일수신체크(ChatHub hub)
        {
            if (_fileReceiveTracker.TryGetValue(hub.FileName, out var info))
            {
                var (fs, received, total) = info;
                long 이전수신량 = received; // 누적되기 전의 기존 수신량 저장

                await fs.WriteAsync(hub.FileData, 0, hub.FileData.Length);
                received += hub.FileData.Length;

                _fileReceiveTracker[hub.FileName] = (fs, received, total);

                if (total > 0)
                {
                    // 방금 받은 데이터로 인해 퍼센트가 어떻게 변했는지 계산
                    int currentPercent = (int)((double)received / total * 100.0);
                    int previousPercent = (int)((double)이전수신량 / total * 100.0);

                    // 20% 단위로 몫을 구함
                    int currentStep = currentPercent / 20;
                    int previousStep = previousPercent / 20;

                    // ★ 핵심: 20% 구간(몫)이 바뀌었거나, 데이터 수신이 100% 끝났을 때만 알림
                    if (currentStep > previousStep || received == total)
                    {
                        Received?.Invoke(this, new ChatEventArgs(this, new ChatHub
                        {
                            RoomId = hub.RoomId,
                            State = ChatState.FileChunk,
                            FileName = hub.FileName,
                            UserName = hub.UserName,
                            Message = $"[수신진행] {hub.UserName} : {hub.FileName} {currentPercent}% ({received / 1024 / 1024.0:F1}MB / {total / 1024 / 1024.0:F1}MB)"
                        }));
                    }
                }
            }
        }
        private async Task Handle파일수신완료(ChatHub hub)
        {
            if (_fileReceiveTracker.TryGetValue(hub.FileName, out var info))
            {
                var (fs, received, total) = info;
                
                try { fs.Flush(); } catch { }
                try { fs.Close(); } catch { }
                try { fs.Dispose(); } catch { }

                _fileReceiveTracker.Remove(hub.FileName);

                Received?.Invoke(this, new ChatEventArgs(this, new ChatHub
                {
                    RoomId = hub.RoomId,
                    State = ChatState.FileEnd,
                    FileName = hub.FileName,
                    UserName = hub.UserName,
                    Message = $"[수신완료] {hub.UserName} : {hub.FileName} ({received / 1024 / 1024.0:F1}MB) *수신Cnt : {hub.Message}*"
                }));
            }

            // 클라이언트로 완료 응답 전송
            if (_isServer)
            {
                Send(new ChatHub
                {
                    RoomId = hub.RoomId,
                    State = ChatState.Ack,
                    Message = $"[서버] 파일 {hub.FileName} 수신 완료"
                });
            }
            await Task.CompletedTask;
        }




        // ============================================================
        // 파일 송신
        // ============================================================

        private const int ChunkSize = 1024 * 1024 ; // 1MB (대용량 안정 + 속도 개선)

        public event Action<string, double> FileProgressChanged;  // 파일명, 진행률(%)
        public event Action<string,string> FileSendCompleted;
        public event Action<string, string> FileStatusChanged;    // 상태 표시: [시작]/[완료]/[오류]

        public async Task SendFileAsync(string filePath, int roomId, string userName, string 플레그, int 파일Cnt)
        {
            FileInfo info = new FileInfo(filePath);
            long totalSize = info.Length;
            long sentSize = 0;
            string fileName = info.Name;

            try
            {
                // 전송 시작 알림
                FileStatusChanged?.Invoke(fileName, "[시작]");

                Send(new ChatHub
                {
                    RoomId = roomId,
                    UserName = userName,
                    FileName = fileName,
                    FileSize = totalSize,
                    플레그 = 플레그,
                    State = ChatState.FileStart
                }); ;

                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[ChunkSize];
                    int bytesRead;
                    long chunkIndex = 0;

                    double progress = 0.0;
                    FileProgressChanged?.Invoke(fileName, progress);

                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        sentSize += bytesRead;

                        Send(new ChatHub
                        {
                            RoomId = roomId,
                            UserName = userName,
                            FileName = fileName,
                            플레그 = 플레그,
                            FileData = buffer.Take(bytesRead).ToArray(),
                            ChunkIndex = chunkIndex++,
                            State = ChatState.FileChunk
                        }); ;

                        progress = (double)sentSize / totalSize * 100.0;
                        FileProgressChanged?.Invoke(fileName, progress);
                    }
                }

                string 파일cnt = $"{파일Cnt:00000}";
                FileSendCompleted?.Invoke(fileName, 파일cnt);

                // 파일 전송 완료 알림
                Send(new ChatHub
                {
                    RoomId = roomId,
                    UserName = userName,
                    FileName = fileName,
                    플레그 = 플레그,
                    Message = 파일cnt,
                    State = ChatState.FileEnd
                });

                
            }
            catch (Exception ex)
            {
                FileStatusChanged?.Invoke(fileName, $"[오류] {ex.Message}");
            }
        }

        public async Task 파일전송(IEnumerable<string> 원본파일들, int roomId, string userName, string 플레그)
        {
            try
            {
                int 파일갯수 = 원본파일들.Count();

                // ✅ 메세지 전달 (StoC, CtoS 모두 가능)
                Send(new ChatHub
                {
                    RoomId = roomId,
                    FileName = "",
                    UserName = userName,
                    State = ChatState.Message,
                    Message = $"[파일갯수] {userName} : 전송파일 갯수 - {파일갯수})"
                });


                int 파일Cnt = 0;
                foreach (var file in 원본파일들)
                {
                    파일Cnt++;
                    await SendFileAsync(file, roomId, userName, 플레그, 파일Cnt);
                    await Task.Delay(100);    // CPU/네트워크 완충 (필요 시)
                }

                // ✅ 메세지 전달 (StoC, CtoS 모두 가능)
                Send(new ChatHub
                {
                    RoomId = roomId,
                    FileName = "",
                    UserName = userName,
                    State = ChatState.Message,
                    Message = $"[파일갯수] {userName} : 완료파일 갯수 - {파일Cnt})"
                });

                //서버가 아니면, 약관이 생성 ==> PDF로 변환 ==> 세션별PDF 병합 ==> "서버로 전송"
                if (플레그 == "C To S : 개별PDF전송")
                {
                    string EndofFile = $"{저장.출력폴더}StateFile\\{userName}.END";
                    string 출력내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일송신] : {1:00000} ", DateTime.Now, 원본파일들.Count());
                    File.WriteAllText(EndofFile, 출력내용, Encoding.GetEncoding(949));
                    await Task.Delay(100);
                    await SendFileAsync(EndofFile, roomId, userName, 플레그,0); //End of File 전송 

                    if (파일갯수 != 파일Cnt)
                    {
                        Send(new ChatHub
                        {
                            RoomId = roomId,
                            FileName = "",
                            UserName = userName,
                            State = ChatState.Message,
                            Message = $"[전송오류] {userName} : === 송신데이터 {파일갯수} < > 수신데이터 {파일Cnt} ===)"
                        });
                    }

                }

                foreach (var file in 원본파일들)
                {
                    new FileInfo(file).Delete();
                    await Task.Delay(10);
                }
            }
            catch (Exception ex)
            {
                string 메세지 = ex.Message;
                
            }
        }

        public void ForceDisconnect()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
        }


    }
}
