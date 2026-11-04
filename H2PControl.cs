using DevExpress.Utils.Svg;
using DevExpress.XtraEditors;
using H2PControl.Events;
using H2PControl.Handlers;
using H2PControl.Models;
using H2PControl.Sockets;
//using iText.Forms;
//using iText.IO.Font;
//using iText.Kernel.Font;
//using iText.Kernel.Geom;
//using iText.Kernel.Pdf;
//using iText.Kernel.Utils;
//using iText.Layout;
//using iText.Layout.Element;
//using iText.Layout.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraGauges.Win.Gauges.State;

namespace H2PControl
{
    public partial class H2PControl : DevExpress.XtraEditors.XtraForm
    {
        private FileStream Logfs;
        private StreamWriter Logwr;
        string 로그내용;
        private int _currentLogDay = -1;
        private static int 실행상태 = 0;
        private ChatServer _server;
        private ClientRoomManager _roomManager;
        private ChatClient _client;
        private ClientHandler _clientHandler;
        public string 실행주체 = "";
        private int C_RoomId;
        private string C_UserName;
        private ListBoxControl[] 리스트;
        private bool[] 리스트깜빡임상태 = new bool[10];
        private DevExpress.XtraEditors.LabelControl[] 타일;
        private Dictionary<string, DateTime> 서브컴작업시작시간 = new Dictionary<string, DateTime>();
        private Dictionary<string, int> 서브컴할당타임아웃 = new Dictionary<string, int>();
        private Dictionary<string, int> 서브컴할당건수 = new Dictionary<string, int>();
        private System.Windows.Forms.Timer 서브컴작업시간타이머 = new System.Windows.Forms.Timer();
        private bool[] errorStates;
        private 환경설정 설정 = new 환경설정();
        private string 파라메타 = "";
        private Dictionary<string, int> 접속컴 = new Dictionary<string, int>();
        private System.Windows.Forms.Timer monitorTimer;
        private bool isRestarting = false;
        private bool 첫실행 = true;
        private System.Drawing.Image[] errorIcon = new System.Drawing.Image[2];
        private StateIndicatorComponent[] f_indicators;
        private string[] _currentStates = new string[8];

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private CancellationTokenSource _h2pCancellationTokenSource;
        private bool 강제종료 = false;
        
        private bool[] F앱상태 = new bool[9]; // 1~8 인덱스 사용
        private bool 로그출력예약됨 = false;
        private object 요약락 = new object();
        private string 기본타이틀 = "";

        private ConcurrentQueue<(DateTime Time, int Index)> F앱에러시간기록 = new ConcurrentQueue<(DateTime Time, int Index)>();
        private volatile bool F앱전체초기화중 = false;
        private static readonly object _circuitBreakerLock = new object();
        private DateTime 최근GlobalFlush시간 = DateTime.MinValue;
        private static readonly object _logLock = new object(); // 로그 동기화 전용 락
        //private static DateTime 글로벌최근성과시간 = DateTime.Now;
        private bool _isRunning = true;
        private bool 분산메인컴 = false;
        private static Dictionary<string, int> 글로벌에러카운트 = new Dictionary<string, int>();

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect,     // x-coordinate of upper-left corner
            int nTopRect,      // y-coordinate of upper-left corner
            int nRightRect,    // x-coordinate of lower-right corner
            int nBottomRect,   // y-coordinate of lower-right corner
            int nWidthEllipse, // 둥근 모서리의 너비 (곡률)
            int nHeightEllipse // 둥근 모서리의 높이 (곡률)
        );

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        private static extern bool DeleteObject(IntPtr hObject);
        private ConcurrentDictionary<string, Task> _재할당작업목록 = new ConcurrentDictionary<string, Task>();

        private int 글로벌동적타임아웃 = 720;

        public H2PControl(string[] args)
        {
            InitializeComponent();
            if (args.Length >= 1)
                파라메타 = args[0];

            string 컴이름 = SystemInformation.ComputerName.ToLower();
            string 고객사 = 고객사선택("", 컴이름);
            if (고객사 == "CCK2") chk싱글처리.Checked = true;
            if (컴이름 == "Kmain") 분산메인컴 = true;

            ////////테스트
            ////chk싱글처리.Checked = true;
            ////if (컴이름 == "Kmain" || 컴이름 == "dzmain") 분산메인컴 = true;
        }

        private void H2PControl_Load(object sender, EventArgs e)
        {
            // 프로그램이 켜져 있는 동안 절대 잠들지 않게 설정
            PreventSleep.DonSleep();
            if (!분산메인컴)
            {
                int X = 0;
                int Y = 484;
                this.Location = new System.Drawing.Point(X, Y);
                this.Size = new System.Drawing.Size(518, 557);
            } else
            {
                this.StartPosition = FormStartPosition.Manual;

                // 작업 표시줄(Taskbar)을 제외한 실제 모니터 작업 영역(WorkingArea)을 가져옵니다.
                System.Drawing.Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;

                int formHeight = this.Height;
                int baseWidth = 518; // ★ 서버 기본 컨트롤(lsth2p 등)만 딱 보이는 아담한 시작 너비

                // ====================================================================
                // ★ 모니터 왼쪽 하단에 딱 붙고, 가로는 기본 너비(518)로 시작합니다.
                // ====================================================================
                this.Bounds = new System.Drawing.Rectangle(workingArea.Left, workingArea.Bottom - formHeight, baseWidth, formHeight);
            }
            this.Resize += H2PControl_Resize;
            this.Opacity = 0.8;
            컨트롤러초기화(); //List, 이미지 초기화
            화면초기화_LED();
            일하는순서();

            // 1. 글자색 및 카이겐고딕 KR 폰트 설정
            // 현재 진행 단계 (Active)
        }

        

        private void 타이틀업데이트()
        {
            // 기본 타이틀이 아직 세팅되지 않았거나 최소화 상태면 무시
            if (string.IsNullOrEmpty(기본타이틀) || this.WindowState == FormWindowState.Minimized)
                return;

            // "원래타이틀 - (X, Y), (Width, Height)" 형식으로 표시
            this.Text = $"{기본타이틀} - ({this.Location.X}, {this.Location.Y}), ({this.Size.Width}, {this.Size.Height})";
        }

        private async void 일하는순서()
        {
            저장.로그경로 = @"c:\Easy약관Plus\program\LogFile\";

            로그파일열기();
            설정 = new 환경설정();
            기본설정(설정);

            for (int i = 1; i <= 8; i++)
            {
                // 멈춤(Freeze) 감지! 오류 아이콘으로 변경
                UpdateFProcessState(i - 1, "WAITING");
            }

            if (설정.병렬처리여부)
            {
                if (설정.상태컴퓨터 == "main")
                {
                    실행주체 = "서버";
                    TabControl.SelectedTabPage = Tabserver;
                }
                else
                {
                    실행주체 = "클라이언트";
                    TabControl.SelectedTabPage = Tabclient;
                }

                //서버/클라이언트 별로 접속을 한다.
                if (await TCPServer_Client초기화(실행주체, "시작", 설정) != "성공")
                {
                    로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[오류] {실행주체} 환경설정 오류";
                    로그기록(로그내용, 1);
                    return;
                }
                else
                {
                    // '맞춤약관앱' 감시를 시작합니다.
                    this.Invoke(new MethodInvoker(delegate
                    {
                        StartMonitoring(설정);
                    }));
                }

                if (실행주체 == "서버")
                {
                    로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[대기] 서브컴 접속 대기 중 (10초)...";
                    로그기록(로그내용, 1);
                    await Task.Delay(TimeSpan.FromSeconds(10)); // 10초 대기
                }
            }

            while (_isRunning)
            {
                try
                {
                    // 강제 재시작 프로세스가 진행 중이면 Input 폴더 건드리지 말고 무한 대기!
                    if (isRestarting)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    시스템상태업데이트(1); // 1단계: 대기 (새로운 파일을 기다림)

                    // 초기화 및 로그파일 관리
                    현재상태 상태 = new 현재상태(설정);
                    if (DateTime.Now.Day != _currentLogDay)
                    {
                        로그파일열기();
                        _ = 오래된파일정리_비동기();
                    }

                    if (chk싱글처리.Checked && 실행주체 == "서버")
                    {
                        await 관제탑_타임아웃복구(상태);
                    }

                    string 분리결과 = await 단계01파일검색_파일분리(상태);

                    if (분리결과 == "정지")
                    {
                        break; // 메인 while(_isRunning) 루프를 즉시 탈출
                    }
                    else if (분리결과 == "실패")
                    {
                        throw new Exception("??진행불가 : 단계01파일검색_파일분리 오류, 로그참조");
                    }

                    //입력데이터 체크
                    if (chk싱글처리.Checked && 실행주체 == "서버" && 분산메인컴)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2)); // CPU 과부하 방지용 짧은 휴식
                        continue;
                    }

                    시스템상태업데이트(2); // 2단계: 진행 (파일을 찾아서 작업 시작)

                    // ==============================================================================
                    // [핵심] 타임아웃 연장(Sliding Timeout) 및 방파제(내부 루프) 로직
                    // ==============================================================================
                    string 고객사 = 고객사선택("", 저장.사용컴퓨터);
                    int 타임아웃분 = 9; // 기본 타임아웃
                    int 총할당건수 = 16; // 기본 할당 건수

                    if (고객사 == "CCK2")
                    {
                        if (상태.groupKey.StartsWith("[M"))
                        {
                            int endIdx = 상태.groupKey.IndexOf(']');
                            if (endIdx > 2)
                            {
                                string 추출문자열 = 상태.groupKey.Substring(2, endIdx - 2);
                                if (int.TryParse(추출문자열, out int parsingResult))
                                {
                                    총할당건수 = parsingResult;
                                }
                            }
                        }
                    }

                    bool 작업완료 = false;
                    int 종료지연_재시도 = 0; // 무한루프(데드락) 방지용 안전장치

                    // ★ 방파제 역할: 타임아웃이 터져도 밖으로 튕겨 나가지 않고 이 루프 안에서 재도전
                    while (!작업완료 && _isRunning)
                    {
                        글로벌동적타임아웃 = 720;

                        // 총할당건수(또는 남은건수)에 따라 타임아웃 시간(분) 정밀 재배정
                        if (고객사 == "CCK2")
                        {
                            // ★ [변경] 이 부분을 아래 코드로 덮어쓰세요!
                            if (총할당건수 <= 4) 타임아웃분 = 15;      // 12분(F1대기) + 3분(여유)
                            else if (총할당건수 <= 8) 타임아웃분 = 20;
                            else if (총할당건수 <= 12) 타임아웃분 = 25;
                            else 타임아웃분 = 30; // 최대 16건일 때 30분 마지노선
                        }

                        TimeSpan 타임아웃시간 = (고객사 == "CCK2") ? TimeSpan.FromMinutes(타임아웃분) : Timeout.InfiniteTimeSpan;

                        using (CancellationTokenSource cts = new CancellationTokenSource(타임아웃시간))
                        {
                            try
                            {
                                // 기존 데이터 처리 및 F1~F8 실행 로직
                                bool 선택약관PDF병합샐행 = false;

                                if (await data처리Table검색(상태, "HWP2PDF"))
                                {
                                    저장.출력폴더 = 상태.출력폴더;

                                    if (await F1실행관리(상태, cts.Token))
                                    {
                                        선택약관PDF병합샐행 = true;
                                        Hwp프로세스싹쓸이();
                                    }
                                }
                                else
                                {
                                    throw new Exception("??진행불가 : data처리Table검색 오류, 로그참조");
                                }

                                if (선택약관PDF병합샐행 || await data처리Table검색(상태, "PDFMERGE"))
                                {
                                    if (await 선택약관PDF병합(상태, cts.Token) == false)
                                    {
                                        throw new Exception("??진행불가 : 선택약관PDF병합 오류, 로그참조");
                                    }
                                }

                                string 단계완료파일 = $"{저장.기본출력폴더}감시\\{상태.groupKey}.단계완료";
                                File.WriteAllText(단계완료파일, "H2PControl 완료", Encoding.GetEncoding(949));

                                if (chk싱글처리.Checked && !분산메인컴)
                                {
                                    BtnStop.Enabled = true;
                                    await 단독Main작업완료대기(상태, cts.Token);
                                }
                                else if (상태.병렬컴 >= 2 || 실행주체 == "클라이언트")
                                {
                                    BtnStop.Enabled = true;
                                    await 서버수신대기_클라이언트파일송신(상태, 실행주체, cts.Token);
                                }
                                else
                                {
                                    await 단독Main작업완료대기(상태);
                                }

                                if (실행주체 == "클라이언트")
                                {
                                    _clientHandler?.Send(new ChatHub
                                    {
                                        RoomId = C_RoomId,
                                        UserName = C_UserName,
                                        Message = "[**작업완료**] 개별약관 작업이 완료 되었습니다.",
                                        상황 = "완료",
                                        State = ChatState.진행상황
                                    });
                                }

                                시스템상태업데이트(3); // 3단계: 완료 (한 사이클 종료)
                                작업완료 = true; // ★ 모든 단계 통과 시 방파제 루프 정상 탈출
                            }
                            catch (OperationCanceledException)
                            {
                                // =========================================================================
                                // ★ [타임아웃 감지] 물리적 성과(완료 파일)를 확인하여 후속 조치 결정
                                // =========================================================================
                                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[타임아웃 방어] 작업 지연 감지. 성과를 분석하여 타임아웃을 재조정합니다.", 1);

                                int 완료건수 = 0;
                                try
                                {
                                    for (int i = 1; i <= 상태.앱실행수; i++)
                                    {
                                        string 완료폴더 = $@"{상태.출력폴더}{i}\@완료\";
                                        if (Directory.Exists(완료폴더))
                                        {
                                            // [수정 핵심] *_02.hwp를 찾되, F1 앱의 죽음 마커인 'Tmp_Tmp_'는 제외하고 진짜 완료 건수만 셉니다.
                                            완료건수 += new DirectoryInfo(완료폴더)
                                                        .GetFiles("*_02.hwp")
                                                        .Count(f => !f.Name.StartsWith("Tmp_Tmp_"));
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[파일카운트 오류] {ex.Message}", 1);
                                }

                                int 남은건수 = 총할당건수 - 완료건수;

                                if (남은건수 <= 0)
                                {
                                    종료지연_재시도++;
                                    if (종료지연_재시도 >= 3)
                                    {
                                        // 병합 단계 등 다른 구간에서 심각한 무한 락이 걸렸을 경우를 대비한 최종 안전장치
                                        throw new TimeoutException($"{타임아웃시간}분 대기 및 20초 쿨다운 3회 연속 실패. 시스템 강제 초기화 진행.");
                                    }

                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[종료 지연] 남은건수 0건. 변환은 완료되었으나 종료 딜레이가 발생했습니다. 20초 쿨다운을 부여합니다.", 1);

                                    // ★ 지적해주신 20초 쿨다운 부여 (F1 감시 루프가 좀비를 잡을 시간 확보)
                                    await Task.Delay(TimeSpan.FromSeconds(20));

                                    // 확실하게 프로세스 싹쓸이로 잔여 락(Lock) 해제
                                    Hwp프로세스싹쓸이();

                                    // 남은 건수를 0으로 세팅하고 continue. 다음 루프에 진입하면 F1실행관리는 즉시 true를 반환하고 PDF 병합 단계로 넘어갑니다.
                                    총할당건수 = 0;
                                    continue;
                                }
                                else
                                {
                                    종료지연_재시도 = 0; // 정상 변환 중이므로 지연 카운트 초기화
                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[타임아웃 연장] 남은건수 {남은건수}건. 해당 건수에 맞춰 타임아웃을 새로 산정합니다.", 1);

                                    // ★ 총할당건수를 남은건수로 업데이트 후 continue. 다음 루프 시작 시 타임아웃 분(minute)이 재계산됩니다.
                                    총할당건수 = 남은건수;
                                    continue;
                                }
                            }
                        } // End of using (CancellationTokenSource)
                    } // End of while (!작업완료)

                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                catch (TimeoutException tEx)
                {
                    // 방파제 루프의 '최종 안전장치'에서 예외를 던지면 원래대로 강제 초기화 실시
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[ERROR] {tEx.Message}", 1);

                    if (실행주체 == "서버")
                    {
                        강제초기화_및_재시작처리(실행주체);
                    }
                    else if (실행주체 == "클라이언트")
                    {
                        try
                        {
                            string 빨간딱지경로 = $"{저장.기본출력폴더}감시\\강제재시작.종료";
                            File.WriteAllText(빨간딱지경로, "클라이언트 로컬 타임아웃 초과. 자체 부활 시도.");
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[자체복구] 로컬 타임아웃으로 인해 조용히 자체 재시작을 시도합니다.", 1);
                        }
                        catch { }
                    }

                    await Task.Delay(Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[ERROR] H2PControl 앱 오류 - {ex.Message}", 1);
                }

                파라메타 = "";
            }

            if (!_isRunning)
            {
                MessageBox.Show("작업이 완전이 종료 되었습니다.", "작업완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }


        // =======================================================================================================================================
        //                                                     [ 프로그램 분리]
        // =======================================================================================================================================

        // ==========================================================================================================
        // 00 StartMonitoring : 각각의 모니터링을 한다.
        // ==========================================================================================================

        public static string 고객사선택(string tmp고객사, string 현재컴)
        {
            string 선택고객사 = tmp고객사;
            //저장.사용컴퓨터
            switch (현재컴)
            {
                case "Kmain":
                case "Ksub1":
                case "Ksub2":
                case "Ksub3":
                case "Ksub4":
                case "Ksub5":
                    선택고객사 = "CCK2";
                    break;
            }
            return 선택고객사;
        }

        private void StartMonitoring(환경설정 설정)
        {
            // H2P앱 자체의 상태를 기록하는 백그라운드 루프 실행
            _h2pCancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => StartH2PHeartBeatLoop(_h2pCancellationTokenSource.Token, 설정));

            // ⭐ [추가할 코드] 메인 프로그램의 '병합 지시'를 감시하는 백그라운드 루프 시작
            _ = Task.Run(() => 최종병합감시루프(_h2pCancellationTokenSource.Token));

            if (monitorTimer == null)
            {
                monitorTimer = new System.Windows.Forms.Timer();
                monitorTimer.Interval = 10000; // 10초마다 체크
                monitorTimer.Tick += MonitorTimer_Tick;
            }
            monitorTimer.Start();
        }

        private async Task StartH2PHeartBeatLoop(CancellationToken token, 환경설정 설정)
        {
            // 파일명만 H2P용으로 변경 (Easy약관앱에게 보내는 생존 신호)
            string alivePath = @"d:\Easy약관Plus\Output\STATUS\H2P감시상태.txt";

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // ====================================================================
                    // [1] H2P 생존 신호 갱신 (Easy약관앱 무단 재시작 방지)
                    // ====================================================================
                    string currentStatus = "RUNNING";
                    string stepInfo = "H2P 정상작동";
                    string content = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{currentStatus}|{stepInfo}";

                    using (StreamWriter writer = new StreamWriter(alivePath, false, Encoding.GetEncoding(949)))
                    {
                        await writer.WriteAsync(content);
                    }

                    // ====================================================================
                    // [2] 파일 존재 체크 후 -> 존재하면 초기화 프로시저로 이동
                    // ====================================================================
                    bool 파일존재여부 = 강제재시작_파일존재체크();
                    if (파일존재여부)
                    {
                        await 강제초기화_프로세스_실행(token, 설정); // 초기화 작업 수행

                        // 작업 완료 후 3초 대기를 건너뛰고 바로 다음 루프로 넘어가 감시 계속 진행
                        continue;
                    }
                }
                catch (TaskCanceledException)
                {
                    // 프로그램 정상 종료 시 루프 탈출
                    break;
                }
                catch
                {
                    // 파일 읽기/쓰기 충돌 등 순간적인 오류는 무시하고 다음 루프에서 재시도
                }

                await Task.Delay(3000, token); // 3초 대기
            }
        }

        private bool 강제재시작_파일존재체크()
        {
            string 감시폴더 = $"{저장.기본출력폴더}감시\\";
            string 신호파일 = System.IO.Path.Combine(감시폴더, "강제재시작.종료");

            // 폴더가 존재하고, 그 안에 신호 파일이 있으면 true 반환
            return Directory.Exists(감시폴더) && File.Exists(신호파일);
        }

        private async Task 강제초기화_프로세스_실행(CancellationToken token, 환경설정 설정)
        {
            isRestarting = true;
            monitorTimer?.Stop();

            if (실행주체 == "서버")
            {
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템] 전체 초기화 프로세스 시작.", 1);

                // 1. 모든 클라이언트에게 강제재시작 명령 전송
                if (_server != null)
                {
                    await _server.서버To클라이언트송신("", new ChatHub
                    {
                        SendType = SendType.ServerToAll,
                        Message = "[명령]전체재시작",
                        State = ChatState.Message,
                        RoomId = 0,
                        UserName = "서버"
                    });
                }

                string[] 조건 = new string[4]{"data","data처리","Err_data","Err_data처리" };
                for (int i = 0; i < 4; i++)
                {
                    string SqlStr = $"DELETE FROM {조건[i]}";
                    await DBUpdate(SqlStr, 설정.dataTable접속);
                }




                // =========================================================================
                // [모드 1] 기존 병렬/일괄 처리 전용 복구 (@완료 폴더 기반)
                // =========================================================================
                if (!chk싱글처리.Checked)
                {
                    try
                    {
                        string 완료최상위폴더 = System.IO.Path.Combine(저장.기본파일폴더, "@완료");
                        if (Directory.Exists(완료최상위폴더))
                        {
                            FileInfo 가장최근파일 = null;
                            DateTime[] 검색대상일자 = new DateTime[] { DateTime.Now, DateTime.Now.AddDays(-1) };

                            foreach (DateTime 검색일 in 검색대상일자)
                            {
                                string 연월 = $"{검색일:yyyy.MM}";
                                string 일 = $"{검색일:dd}";

                                List<FileInfo> 대상파일들 = new List<FileInfo>();

                                // 고객사가 KB로 고정되어 있으므로 루프 없이 다이렉트로 경로 조립
                                string 타겟폴더 = System.IO.Path.Combine(완료최상위폴더, "CCK", 연월, 일);

                                if (Directory.Exists(타겟폴더))
                                {
                                    대상파일들.AddRange(new DirectoryInfo(타겟폴더).GetFiles("*.Re", SearchOption.TopDirectoryOnly));
                                }

                                if (대상파일들.Count > 0)
                                {
                                    가장최근파일 = 대상파일들.OrderByDescending(f => f.Name).FirstOrDefault();
                                    break;
                                }
                            }

                            if (가장최근파일 != null)
                            {
                                if (가장최근파일.Name.EndsWith(".Re.Re", StringComparison.OrdinalIgnoreCase))
                                {
                                    로그기록($"[재처리무시] 이미 1회 재시도 실패한 파일입니다: {가장최근파일.Name}", 1);
                                }
                                else
                                {
                                    int hashIndex = 가장최근파일.Name.IndexOf('#');
                                    if (hashIndex != -1)
                                    {
                                        string 새파일명 = 가장최근파일.Name.Substring(hashIndex);
                                        string 대상경로 = System.IO.Path.Combine(저장.기본파일폴더, 새파일명);

                                        가장최근파일.CopyTo(대상경로, true);
                                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [자동재처리] 최근 작업 파일을 복구합니다: {새파일명}", 1);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [재처리 오류] 파일 복구 중 예외 발생: {ex.Message}", 1);
                    }
                }

                // =========================================================================
                // [모드 2] 새로운 싱글처리 전용 복구 (TmpInput의 서브컴 찌꺼기 콜백)
                // =========================================================================
                if (chk싱글처리.Checked)
                {
                    try
                    {
                        string tmpInputPath = $@"{저장.기본출력폴더}감시\TmpInput\";
                        if (Directory.Exists(tmpInputPath))
                        {
                            foreach (FileInfo backupFile in new DirectoryInfo(tmpInputPath).GetFiles("*.*"))
                            {
                                string[] 구출데이터 = File.ReadAllLines(backupFile.FullName, Encoding.GetEncoding(949));
                                if (구출데이터.Length > 0)
                                {
                                    int hashIndex = backupFile.Name.IndexOf('#');
                                    string 고객사 = "CCK"; // 기본값
                                    if (hashIndex != -1 && backupFile.Name.Length > hashIndex + 3)
                                        고객사 = backupFile.Name.Substring(hashIndex + 1, 2);

                                    string 복구파일명 = $"{저장.기본파일폴더}#{고객사}_수동복구_{DateTime.Now:HHmmssFFFF}.lst";
                                    File.WriteAllLines(복구파일명, 구출데이터, Encoding.GetEncoding(949));
                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [자동재처리] 서브컴이 작업 중이던 데이터를 즉시 복구합니다: {backupFile.Name}", 1);
                                }
                                backupFile.Delete(); // 구출 완료 후 TmpInput 찌꺼기 삭제
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [재처리 오류] TmpInput 복구 중 예외 발생: {ex.Message}", 1);
                    }
                }


            }
            else if (실행주체 == "클라이언트")
            {
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템] 서버 명령에 따라 로컬 초기화 프로세스 시작.", 1);
            }

            폴더내부파일모두삭제();
            await F1종료모두종료();

            if (chk싱글처리.Checked && !분산메인컴)
            {

                // protected override void OnFormClosing(FormClosingEventArgs e) 동일한 로직
                try
                {
                    string dbConnString = (설정 != null && !string.IsNullOrEmpty(설정.dataTable접속))
                                          ? 설정.dataTable접속
                                          : "Server=localhost; Database=약관; Integrated Security=True;";
                    string[] tables = { "data", "data처리", "Err_data", "Err_data처리" };

                    using (SqlConnection conn = new SqlConnection(dbConnString))
                    {
                        conn.Open();
                        foreach (string table in tables)
                        {
                            new SqlCommand($"DELETE FROM {table}", conn).ExecuteNonQuery();
                        }
                    }
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템] 서브컴 로컬 DB 찌꺼기 청소 완료.", 1);


                    // 감시 및 List 폴더 비우기
                    string baseOutPath = @"d:\Easy약관Plus\";
                    string[] targetDirs = { baseOutPath + @"Output\감시\", baseOutPath + @"List\" };

                    foreach (string dir in targetDirs)
                    {
                        if (System.IO.Directory.Exists(dir))
                        {
                            foreach (string file in System.IO.Directory.GetFiles(dir))
                            {
                                try { System.IO.File.Delete(file); } catch { } // 잠겨있는 파일 예외 무시
                            }
                        }
                    }


                }
                catch { }

            }

            if (!(chk싱글처리.Checked && 분산메인컴))
            {
                bool success = await Easy약관Plus실행();
            }
            else
            {
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템] 관제탑 모드이므로 Easy약관Plus를 재실행하지 않습니다.", 1);
            }

            // 앱 종료 및 초기화 후 프로세스가 엉키지 않도록 5초 여유 대기
            await Task.Delay(5000, token);
            isRestarting = false;
            monitorTimer?.Start();
        }



        private async void MonitorTimer_Tick(object sender, EventArgs e)
        {
            if (isRestarting) return;

            if (chk싱글처리.Checked && 분산메인컴)
            {
                return;
            }

            // 감시 대상 파일 경로
            string statusPath = @"d:\Easy약관Plus\Output\STATUS\앱감시상태.txt";
            string finalStatus = "OFF";
            string finalDetail = "";
            bool needsRestart = false; // 재시작 필요 여부

            //테스트용
            if (강제종료)
                needsRestart = true;

            try
            {
                if (File.Exists(statusPath))
                {
                    // 파일 읽기 (다른 프로세스 사용 중일 수 있으니 공유 모드로 읽기)
                    using (var fs = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, Encoding.GetEncoding(949)))
                    {
                        string content = sr.ReadToEnd();
                        string[] parts = content.Split('|'); // [0]시간, [1]상태, [2]내용

                        if (parts.Length >= 2)
                        {
                            DateTime lastTime = DateTime.Parse(parts[0]);
                            string appState = parts[1];
                            string appDetail = parts.Length > 2 ? parts[2] : "";

                            // ★ 상태 판단 로직
                            TimeSpan diff = DateTime.Now - lastTime;

                            if (diff.TotalSeconds > 180) // 180초 이상 갱신 안됨 -> 멈춤, 재시작 고려
                            {
                                finalStatus = "FROZEN";
                                finalDetail = $"응답없음({diff.TotalSeconds:F0}초)";
                                needsRestart = true; // 재시작 플래그 ON
                            } 
                            else // WAITING, RUNNING, ERROR, RECORDERR
                            {
                                finalStatus = appState;
                                finalDetail = appDetail;

                                if (appState == "리스타트앱")
                                    needsRestart = true;
                            }
                        }
                    }
                }
            }
            catch
            {
                finalStatus = "CHECK_FAIL"; // 파일 읽기 실패
            }

            if (실행주체 == "클라이언트")
            {
                // 서버로 전송
                // _clientHandler가 연결되어 있을 때만 보냅니다.
                if (_clientHandler != null && (finalStatus == "FROZEN" || 
                        finalStatus == "ERROR" || finalStatus == "RECORDERR" || finalStatus == "리스타트앱"))
                {
                    // 룸ID에 맞춰서 메시지를 보냅니다.
                    // 서버는 이 RoomId를 보고 lstH2P_1 ~ 5 중 어디에 뿌릴지 결정합니다.
                    string 서버보고상황 = (finalStatus == "리스타트앱") ? "재시작" : "오류";
                    _clientHandler.Send(new ChatHub
                    {
                        RoomId = C_RoomId,                            // 예: 1 (Sub1)
                        UserName = C_UserName,                        // 예: Sub1
                        State = ChatState.진행상황,                   // 상태 업데이트용으로 사용
                        상황 = 서버보고상황,                                
                        Message = $"[{finalStatus}]{finalDetail}"     // Step-03, 오류내용 ...
                    });
                }
            }

            if (finalStatus == "FROZEN" || finalStatus == "ERROR" || finalStatus == "RECORDERR")
            {
                string message = $"[{finalStatus}]{finalDetail}";
                int 표시 = 1;
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}{message}", 표시, 0);
                SetError(true, 0, finalStatus);
            }
            else if (finalStatus == "리스타트앱")
            {
                // 정기 재시작은 에러(SetError)로 처리하지 않고 INFO 로그만 남깁니다.
                string message = $"{DateTime.Now:yy.MM.dd HH:mm:ss} [INFO] 정기 재시작 요청을 감지하여 앱을 재실행합니다.";
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}{message}", 1, 0);
            }

            if (needsRestart) 
            {
                // 중복 실행 방지 락 걸기
                isRestarting = true;
                monitorTimer.Stop();

                for (int i = 1; i <= 8; i++)
                    await F1프로그램종료(i);

                try
                {
                    if (실행주체 == "클라이언트" && _client != null)
                    {
                        _client.Close(); // 클라이언트 모드일 때 서버와 연결 끊기
                        await Task.Delay(1000); // 끊김 패킷이 날아갈 시간 확보
                    }
                    else if (실행주체 == "서버" && _server != null)
                    {
                        // 1. 방에 있는 모든 서브 PC의 연결선을 직접 가위로 자릅니다!
                        // (이 코드가 실행되어야 서브 PC 화면에 "접속이 끊겼습니다"가 뜹니다)
                        if (_roomManager != null)
                        {
                            _roomManager.DisconnectAllClients();
                        }

                        // 2. 그 다음 문지기를 퇴근시킵니다.
                        _server.Stop();

                        await Task.Delay(1000); // 끊김 패킷이 날아갈 시간 확보
                    }
                }
                catch
                {
                    // 오류가 나도 무시 (어차피 앱을 재시작할 것이므로)
                }

                bool success = await Easy약관Plus실행();
                await Task.Delay(10000*6); // 실행시간 때문에 10*6초대기 
                                           // 감시 재개
                isRestarting = false;
                monitorTimer.Start();
            }
        }

        // ==========================================================================================
        // [관제탑 전용] 서브컴 10분 타임아웃 감시 및 데이터 복구 / 원격 재시작 프로시저
        // ==========================================================================================
        private async Task 관제탑_타임아웃복구(현재상태 상태)
        {
            try
            {
                string tmpInputPath = $@"{저장.기본출력폴더}감시\TmpInput\";
                if (!Directory.Exists(tmpInputPath)) Directory.CreateDirectory(tmpInputPath);

                foreach (FileInfo backupFile in new DirectoryInfo(tmpInputPath).GetFiles("*.*"))
                {
                    string 고장난컴 = backupFile.Extension.TrimStart('.'); // 예: sub5
                    int 적용타임아웃 = 10; // 기본값
                    if (서브컴할당타임아웃.ContainsKey(고장난컴))
                    {
                        적용타임아웃 = 서브컴할당타임아웃[고장난컴];
                    }

                    // 파일이 생성(복사)된 지 10분이 넘었는지 체크
                    if ((DateTime.Now - backupFile.LastWriteTime).TotalMinutes >= 적용타임아웃)
                    {

                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[관제탑복구] {고장난컴} 응답 없음(10분 초과). 데이터 회수 및 원격 재시작 진행.", 1);

                        // [1] 데이터 구출 (원본 Input 폴더로 반환)
                        try
                        {
                            string[] 구출데이터 = File.ReadAllLines(backupFile.FullName, Encoding.GetEncoding(949));
                            if (구출데이터.Length > 0)
                            {
                                int hashIndex = backupFile.Name.IndexOf('#');
                                string 고객사 = "CCK"; // 기본값
                                if (hashIndex != -1 && backupFile.Name.Length > hashIndex + 3)
                                    고객사 = backupFile.Name.Substring(hashIndex + 1, 2);

                                string 복구파일명 = $"{저장.기본파일폴더}#{고객사}_타임아웃복구_{DateTime.Now:HHmmssFFFF}.lst";
                                File.WriteAllLines(복구파일명, 구출데이터, Encoding.GetEncoding(949));
                            }
                        }
                        catch (Exception ex)
                        {
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[구출실패] 파일 읽기/쓰기 오류: {ex.Message}", 1);
                        }

                        // [2] TmpInput에서 찌꺼기 파일 삭제 (해당 서브컴 빈손 처리)
                        try { backupFile.Delete(); } catch { }

                        // [3] 원격 사살(재시작) 명령 하달
                        if (접속컴.TryGetValue(고장난컴, out int roomID))
                        {
                            await _server.서버To클라이언트송신(고장난컴, new ChatHub
                            {
                                SendType = SendType.ServerToOne,
                                State = ChatState.Message,
                                Message = "[명령]전체재시작",
                                플레그 = "S To C : 원격재시작명령",
                                UserName = 상태.상태컴퓨터,
                                RoomId = 0
                            });
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[관제탑명령] {고장난컴} 강제재시작 명령 하달 완료.", 1);
                        }
                        else
                        {
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[관제탑명령] {고장난컴} 접속 끊김 (네트워크 단절로 명령 불가)", 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[관제탑복구 오류] {ex.Message}", 1);
            }
        }




        // ==========================================================================================================
        // 01 기본화면설정 : 스레드갯수 설정
        // ==========================================================================================================
        private void 기본설정(환경설정 설정)
        {

            string 임시컴 = SystemInformation.ComputerName.ToLower();
            if (파라메타 == "앱실행_싱글처리")
            {
                if (임시컴.StartsWith("Ksub"))
                    저장.사용컴퓨터 = "Kmain";
                else if (임시컴.StartsWith("sub"))
                    저장.사용컴퓨터 = "main";
                else if (임시컴.StartsWith("dzsub"))
                    저장.사용컴퓨터 = "dzmain";
                else
                    저장.사용컴퓨터 = 임시컴;
            }
            else
                저장.사용컴퓨터 = 임시컴;

            //디폴트 저장 인수값
            저장.DB설정파일path = @"c:\Easy약관Plus\program\localDB.txt";
            저장.기본출력폴더 = @"d:\Easy약관Plus\Output\";
            저장.기본입력폴더 = @"d:\Easy약관Plus\List\";
            저장.기본파일폴더 = @"d:\Easy약관Plus\Input\";


            //모두 3곳을 수정해야 한다.(맞춤약관, H2PControl, F1)
            Dictionary<string, string> 설정값 = new Dictionary<string, string>();
            if (new System.IO.FileInfo(저장.DB설정파일path).Exists)
            {
                string[] 설정파일 = System.IO.File.ReadAllLines(저장.DB설정파일path, Encoding.GetEncoding(949));
                foreach (string tmp설정 in 설정파일)
                {
                    string[] 분리 = tmp설정.Split('\t');
                    if (분리.Length < 2) continue;

                    string key = 분리[0].Trim();
                    string value = 분리[1].Trim();
                    if (key == "접속" || key == "FTP" || key == "소켓" || key == "모바일" || key == "약관" || key == "PDF")
                    {
                        if (분리.Length < 3) continue;

                        string 키1 = 분리[1].Trim();
                        string 값1 = 분리[2].Trim();

                        if (key == "접속" || key == "소켓") {
                            키1.Split('|')
                               .ToList()
                               .ForEach(x =>
                               {
                                   if (!설정값.ContainsKey($"{key}{x}"))
                                       설정값.Add($"{key}{x}", 값1);
                               });
                        } else if (key == "FTP") {
                            if (!설정값.ContainsKey($"{key}{키1}"))
                                설정값.Add($"{key}{키1}", 값1);
                        }
                        
                    }
                    else if (key == "병렬") {
                        value.Split('|')
                             .Where(x => x.Contains("main") || x.Contains("sub"))
                             .ToList()
                             .ForEach(x =>
                             {
                                 if (!설정값.ContainsKey(($"{key}{x}")))
                                     설정값.Add($"{key}{x}", x.Contains("main") ? "main" : "sub");
                             });
                    } else if (key == "위치크기정보")
                    {
                        if (분리.Length >= 3)
                        {
                            string 키값 = $"{분리[0]}{분리[1]}";
                            string 위치정보 = 분리[2];

                            if (!설정값.ContainsKey(키값))
                            {
                                설정값.Add(키값, 위치정보);
                            }
                        }
                    }
                    else {
                        if (!설정값.ContainsKey(key))
                            설정값.Add(key.ToUpper(), value);
                    }
                }
            }

            try
            {
                string 컴유형 = "";
                if (설정값.TryGetValue($"병렬{저장.사용컴퓨터}", out string tmp컴유형))
                    컴유형 = tmp컴유형;

                설정.병렬처리여부 = 컴유형 == "" ? false : true;

                //모두 3곳을 수정해야 한다.(맞춤약관, H2PControl, F1)
                switch (컴유형)
                {
                    //병렬처리
                    case "sub":
                        //끝에서 4글자
                        설정.상태컴퓨터 = 저장.사용컴퓨터.Substring(저장.사용컴퓨터.Length - 4, 4); // ex) sub1, sub2, sub3,..... 반드시 병렬컴퓨터는 컴퓨터명을 sub포함해야 한다..
                        int 인수 = 설정.상태컴퓨터.IndexOf("sub") + 3;
                        int 숫자 = -1;
                        if (int.TryParse(설정.상태컴퓨터.Substring(인수), out 숫자))
                        {
                            설정.처리구분 = string.Format("{0:00}", 숫자);
                        }
                        C_RoomId = 숫자;
                        C_UserName = 설정.상태컴퓨터;
                        break;
                    default:
                        설정.상태컴퓨터 = "main";   // main컴, 일반컴 모두 main컴으로 처리
                        설정.처리구분 = "00";
                        break;
                }

                //data테이블 접속관련
                if (chk싱글처리.Checked || 파라메타 == "앱실행_싱글처리")
                {
                    설정.dataTable접속 = 설정값["접속LOCALDB"];
                    로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[DB연결] 싱글처리 모드 - LOCALDB로 연결합니다.";
                    로그기록(로그내용, 1);
                }
                else if (설정값.TryGetValue($"접속{저장.사용컴퓨터}", out string 접속))
                {
                    설정.dataTable접속 = 접속;
                }
                else
                {
                    설정.dataTable접속 = 설정값["접속LOCALDB"];
                }

                //추후 KB약관을 따로 분리할때 고려할것
                //ex)약관KB2
                if (설정값.TryGetValue($"약관전체", out string 약관))
                    저장.약관DB = 약관;



                // ======= [ H2PControl만 해당 ] =======
                //소켓통신 접속관련
                if (파라메타 == "앱실행_싱글처리")
                {
                    //로컬IP로 대체한다.
                    설정.접속IP = "127.0.0.1";
                    설정.접속PORT = 8080;
                } else {
                    if (설정값.TryGetValue($"소켓{저장.사용컴퓨터}", out string 소켓))
                    {
                        string[] 소켓분리 = 소켓.Split('|');
                        if (소켓분리.Length == 2)
                        {
                            설정.접속IP = 소켓분리[0].Trim();
                            설정.접속PORT = Convert.ToInt32(소켓분리[1]);
                        }
                    }
                }

                if (설정.상태컴퓨터 == "main")
                    this.Text = "H2PControl (With 맟춤약관)   SERVER";
                else
                    this.Text = $"H2PControl (With 맞춤약관)   CLIENT[{설정.상태컴퓨터}]";

                기본타이틀 = this.Text;
                타이틀업데이트();

                if (분산메인컴) //|| 저장.사용컴퓨터.ToLower() == "dzmain"
                {
                    //////////if (설정값.TryGetValue("위치크기정보2", out string 위치사이즈문자열))
                    //////////{
                    //////////    // 저장해둔 "100|100|500|500" 형태의 문자열을 다시 분리
                    //////////    string[] pos = 위치사이즈문자열.Split('|');

                    //////////    if (pos.Length >= 4)
                    //////////    {
                    //////////        if (int.TryParse(pos[0], out int x) &&
                    //////////            int.TryParse(pos[1], out int y) &&
                    //////////            int.TryParse(pos[2], out int w) &&
                    //////////            int.TryParse(pos[3], out int h))
                    //////////        {
                    //////////            // UI 스레드에서 안전하게 폼 속성 변경
                    //////////            this.WindowState = FormWindowState.Normal;
                    //////////            this.StartPosition = FormStartPosition.Manual;

                    //////////            // 위치(Location)와 크기(Size)를 한 번에 묶어서 강제 적용
                    //////////            this.Bounds = new System.Drawing.Rectangle(x, y, w, h);

                    //////////        }
                    //////////    }
                    //////////}
                }

            }
            catch (Exception ee)
            {
                로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[기본설정 Er02] {1}==>{2}", DateTime.Now, ee.Message, ee.StackTrace);
                로그기록(로그내용, 1);
            }
        }

        // ==========================================================================================================
        //  02 -01  *.단계02파일 검색
        // ==========================================================================================================
        private Dictionary<string, int> 파일오류카운트 = new Dictionary<string, int>();
        private async Task<string> 단계01파일검색_파일분리(현재상태 상태)
        {
            int 재시도 = 0;
            string 실행여부 = "";

            // 재시작     : 컴이 다운현상으로     ==> 감시\*.단계02
            // 오류재처리 : 오류로 인해 다시 처리 ==> @groupKey 파일

            //재시작(끊김현상대비)
            DirectoryInfo dir = new DirectoryInfo($"{저장.기본출력폴더}감시\\");
            foreach (FileInfo 체크파일 in dir.GetFiles("*.단계02"))
            {
                if (new FileInfo(체크파일.FullName).Exists && 파라메타 == "앱실행_재시작")
                {
                    string 내용 = File.ReadAllText(체크파일.FullName, Encoding.GetEncoding(949));
                    string[] 분리 = 내용.Split('|');
                    if (분리.Length == 3)
                    {
                        상태.고객사 = 분리[0].Trim();
                        상태.groupKey = 분리[1].Trim();
                        상태.출력폴더 = 분리[2].Trim();
                        실행여부 = "성공";
                        return 실행여부;
                    }
                }
            }

            //생성이유 : 오류재처리시 H2P앱에서 '단계01파일검색_파일분리'에서 약관생성(03|완료) 이후 이면 
            //           '단계01'(약관생성에서 생성됨) 생성되지 않아 무한 대기 상태
            // 약관생성(상태) 메소드 다음부분 참조
            int 단계03 = dir.GetFiles("*.단계03").Count();
            if (단계03 > 0)
            {
                실행여부 = "성공";
                return 실행여부;
            }

            string[] tmp감시 = new string[2];
            tmp감시[0] = $"{저장.기본출력폴더}감시\\";
            tmp감시[1] = 저장.기본파일폴더;  //@"d:\Easy약관Plus\Input\";
            bool 오류재처리여부 = false;
            bool i1_작업완료 = false; // 파일 분배나 오류 처리가 한 번이라도 실행되었는지 체크하는 플래그
            
            //우선순위 : 단계01 ==> 파일분리
            while (true)
            {
                if (!_isRunning)
                {
                    실행여부 = "정지";
                    return 실행여부;
                }

                int tmp병렬cnt = lbxClients.Items.Count; //main만 해당
                try
                {
                    for(int i = 0; i < tmp감시.Length; i++)
                    {
                        // 이미 분배나 오류처리를 했다면, i=1 폴더에 새 파일이 들어와도 무시하고 i=0(단계01)만 감시합니다.
                        // 분배나 오류처리로직을 수행하면 현재작업이 완료 될때가지 실행하면 않됨
                        if (i == 1 && i1_작업완료 == true)
                            continue;

                        DirectoryInfo 감시폴더 = new DirectoryInfo(tmp감시[i]);
                        string 필터 = i == 0 ? "*.단계01" : "*.*";
                        int 작업파일갯수 = 감시폴더.GetFiles(필터).Count();

                        if (작업파일갯수 == 0)
                            continue;

                        string 오류재처리필터 = "@*.*"; // @ : 재처리 파일
                        

                        //실행순서 : I = 1 (파일분리) ==> I = 0  (PDF변환 이동)
                        if (i == 0 && 작업파일갯수 > 0 && 실행여부 != "단계01") // 맞춤약관 실행 후 단계01 ==> 단계02로 변경 하면서 작업 시작
                        {
                            if (chk싱글처리.Checked && 분산메인컴)
                                continue;

                            foreach (FileInfo 감시파일 in 감시폴더.GetFiles(필터))
                            {
                                상태.groupKey = System.IO.Path.GetFileNameWithoutExtension(감시파일.Name); //확장자제거

                                string 단계02파일 = 감시파일.FullName.Replace("단계01", "단계02");
                                new FileInfo(감시파일.FullName).CopyTo(단계02파일, true);
                                new FileInfo(감시파일.FullName).Delete();

                                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[작업시작] 선택약관파일(hwp)를 PDF로 변환를 시작합니다.";
                                로그기록(로그내용, 1);
                                this.Opacity = 0.8;
                                실행여부 = "단계01";

                                if (실행주체 == "클라이언트")
                                {
                                    _clientHandler?.Send(new ChatHub
                                    {
                                        RoomId = C_RoomId,
                                        UserName = C_UserName,
                                        Message = "[작업시작] 선택약관파일(hwp)를 PDF로 변환를 시작합니다.",
                                        상황 = "처리",
                                        State = ChatState.진행상황
                                    });
                                } 
                                break;
                            }
                        }
                        else if (i == 1 && 상태.상태컴퓨터 == "main" && 오류재처리여부 == false &&
                            감시폴더.GetFiles(오류재처리필터).Count() > 0  ) // 재처리
                        {
                            // 진입 시 플래그를 true로 변경하여 다음 while 루프부터 i=1을 무시하도록 잠금
                            i1_작업완료 = true;

                            foreach (FileInfo 감시파일 in 감시폴더.GetFiles(오류재처리필터))
                            {
                                상태.groupKey = 감시파일.Name.Substring(1);
                                if (tmp병렬cnt >= 1)
                                {
                                    await _server.서버To클라이언트송신("모두", new ChatHub
                                    {
                                        SendType = SendType.ServerToAllFile,
                                        State = ChatState.FileStart,
                                        FileFrom = 저장.기본파일폴더,
                                        플레그 = "S To C : LIST파일전송",
                                        FileFilter = $"@*.*",
                                        UserName = 상태.상태컴퓨터,
                                        RoomId = 0
                                    });
                                }
                                this.Opacity = 0.8;
                                //실행여부 = "단계01";
                                오류재처리여부 = true; // 1번만 처리 함
                                await Task.Delay(TimeSpan.FromSeconds(3));
                                bool 복사여부 = 복사_이동_삭제("복사_삭제", 감시파일.FullName, $"{저장.기본입력폴더}{감시파일.Name}"); //클라이언트로 전송 후 삭제
                                break;
                            }
                        }
                        else if (i == 1 && tmp병렬cnt >= 1 &&  작업파일갯수 > 0 && 상태.상태컴퓨터 == "main" && 실행여부 != "파일분리")
                        {
                            if (chk싱글처리.Checked && 분산메인컴)
                            {
                                int 놀고있는컴수 = 0;
                                for (int c = 0; c < lbxClients.Items.Count; c++)
                                {
                                    string 컴구분 = lbxClients.Items[c].ToString();
                                    // lbJobs(작업중 목록)에 이름이 없으면 놀고 있는 것!
                                    if (!lbJobs.Items.Contains(컴구분))
                                    {
                                        놀고있는컴수++;
                                    }
                                }

                                // 놀고 있는 서브컴이 단 1대도 없다면?
                                if (놀고있는컴수 == 0)
                                {
                                    // 플래그를 풀고, CPU 과부하를 막기 위해 1초 대기 후 다음 루프로 패스!
                                    // (이 덕분에 아래에 있는 파일 이동, 파일 읽기 로직이 아예 실행되지 않습니다)
                                    i1_작업완료 = false;
                                    await Task.Delay(1000);
                                    continue;
                                }
                            }

                            // 진입 시 플래그를 true로 변경하여 다음 while 루프부터 i=1을 무시하도록 잠금
                            i1_작업완료 = true;

                            List<string> tmp작업파일 = new List<string>();
                            string tmp고객사 = "";
                            bool 처음저장 = false;

                            string 이동폴더 = "";
                            string 이동파일 = "";
                            이동폴더 = 폴더생성(저장.기본파일폴더, "@완료");
                            이동폴더 = 폴더생성(이동폴더, "@파일규칙오류");


                            // =========================================================================
                            //  먼저 들어온 파일 우선 정렬(FIFO) 및 Kmain 96개 제한 로직
                            // =========================================================================
                            // 1. 파일을 생성 시간(먼저 들어온 순서) 기준으로 오름차순 정렬합니다.
                            var 대상파일목록 = 감시폴더.GetFiles(필터)
                                    .Where(f => (DateTime.Now - f.LastWriteTime).TotalSeconds >= 2)
                                    .OrderBy(f => f.CreationTime)
                                    .AsEnumerable();

                            if (chk싱글처리.Checked == false && 분산메인컴)
                            {
                                // 확장자가 .Re로 끝나는 파일(강제초기화 프로세스가 복구해둔 파일)이 있는지 확인합니다.
                                var 복구된파일목록 = 대상파일목록.Where(f => f.Name.EndsWith(".Re", StringComparison.OrdinalIgnoreCase)).ToList();

                                if (복구된파일목록.Count > 0)
                                {
                                    // .Re 파일이 존재하면, 이미 여러 건이 묶여있는 상태이므로 
                                    // 96개를 모으지 않고 가장 먼저 들어온 .Re 파일 딱 1개만 뽑아서 최우선으로 단독 처리합니다.
                                    대상파일목록 = 복구된파일목록.Take(1);

                                    로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[재처리] 복구된 .Re 병합파일 1개를 우선 단독 처리합니다.";
                                    로그기록(로그내용, 1);
                                }
                                else
                                {
                                    // .Re 파일이 없다면 평소처럼 상위 96개를 추출하여 묶음 처리를 진행합니다.
                                    대상파일목록 = 대상파일목록.Take(96);
                                }
                            }

                            // 3. 필터링된 파일 목록으로 루프를 돕니다.
                            foreach (FileInfo 파일 in 대상파일목록)
                            {
                                if (파일.Name.IndexOf('#') != -1) // 고객사코드는 반드시 #으로 시작한다.
                                    상태.고객사 = 파일.Name.Substring(파일.Name.IndexOf('#') + 1, 2);
                                else
                                {
                                    이동파일 = string.Format("{0}{1}", 이동폴더, 파일.Name);
                                    bool 복사여부 = 복사_이동_삭제("복사_삭제", 파일.FullName, 이동파일);
                                    if (복사여부 == false)
                                        throw new Exception($"파일복사/삭제 오류 : { 파일.FullName}");

                                    로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[오류] 고객사코드는 반드시 #으로 시작 : {1}", DateTime.Now, 파일.Name);
                                    로그기록(로그내용, 1);
                                    continue;
                                }

                                // 처음파일을 기준으로 동일한 고객사를 묶어 한꺼번에 처리 한다.
                                if (처음저장 == false)
                                {
                                    tmp고객사 = 상태.고객사;
                                    처음저장 = true;
                                }

                                if (tmp고객사 == 상태.고객사)
                                {
                                    tmp작업파일.Add(파일.FullName);

                                    if (상태.고객사 == "CCD") // DB손해보험 파일명을 기본키값으로 사용해서 Merge를 하지 않는다.(모바일)
                                        break;
                                }
                            }

                            if (tmp작업파일.Count == 0)
                            {
                                i1_작업완료 = false; // ★ [핵심 해결] 락(Lock)을 다시 풀어주어야 다음 턴에(2초 뒤) 읽을 수 있습니다.
                                continue;
                            }

                            상태.고객사 = tmp고객사;  // 여러고객사 파일 들어 오면 처리하는 고객사로 원위치 한다.

                            string 처리날짜 = string.Format("{0:yyMMdd}", DateTime.Now);
                            // @완료 폴더생성
                            string 임시이동 = $"@완료\\{상태.고객사}\\{DateTime.Now:yyyy.MM}\\{DateTime.Now:dd}\\";
                            이동폴더 = 폴더생성(저장.기본파일폴더, 임시이동);

                            string 처리파일 = "";
                            string PK중복방지 = string.Format("{0:yyMMdd_HHmmssFFFF}", DateTime.Now);
                            //삭제2 : 리스트파일 삭제(이동)
                            //삭제2 : 리스트파일 삭제(이동) 및 병합 로직 시작
                            if (tmp작업파일.Count == 1)
                            {
                                string 원본파일경로 = tmp작업파일[0];

                                // 1. 에러 3회 이상(999) 누적된 파일은 작업에서 완전 제외 (무한 루프 방지)
                                if (파일오류카운트.TryGetValue(원본파일경로, out int errCnt) && errCnt >= 999)
                                {
                                    이동파일 = ""; // 아래 하위 로직을 타지 않게 비워둠
                                }
                                else
                                {
                                    처리파일 = new FileInfo(원본파일경로).Name;
                                    이동파일 = 이동폴더 + string.Format("{0}_{1}.Re", PK중복방지, 처리파일);

                                    // 2. 먼저 이동(삭제)을 시도하여 파일 소유권(Lock 여부)을 획득합니다.
                                    bool 복사여부 = 복사_이동_삭제("복사_삭제", 원본파일경로, 이동파일);
                                    if (복사여부 == true)
                                    {
                                        // 성공 시 에러 딕셔너리에서 이력 삭제
                                        파일오류카운트.Remove(원본파일경로);
                                    }
                                    else
                                    {
                                        // 3. 이동 실패 (Lock 걸림) -> 카운트 증가
                                        int 현재카운트 = 파일오류카운트.ContainsKey(원본파일경로) ? 파일오류카운트[원본파일경로] : 0;
                                        현재카운트++;
                                        if (현재카운트 >= 3) 현재카운트 = 999;
                                        파일오류카운트[원본파일경로] = 현재카운트;

                                        로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일Lock] 단일파일 이동 실패, 재시도({1}/3) 대기 : {2}", DateTime.Now, 현재카운트 == 999 ? 3 : 현재카운트, 처리파일);
                                        로그기록(로그내용, 1);

                                        이동파일 = ""; // 실패했으므로 하위 로직 진행을 막기 위해 비워둠
                                    }
                                }
                            }
                            else
                            {

                                // 여러개의 파일 존재하면 대표파일명을 새롭게 작성한다.
                                처리파일 = string.Format("#{0}_Merge({1:000}EA).lst", 상태.고객사, tmp작업파일.Count);

                                await Task.Delay(TimeSpan.FromSeconds(1)); // 파일 시스템 안정화를 위한 짧은 대기

                                List<string> 출력내용 = new List<string>();
                                List<string> 안전하게이동된파일목록 = new List<string>();
                                int 파일cnt = 0;

                                // =========================================================
                                // [STEP 1] 읽기 전에 "이동(삭제)"부터 시도하여 소유권을 획득합니다.
                                // =========================================================
                                foreach (string 원본파일경로 in tmp작업파일)
                                {
                                    // 1. 에러 3회 이상(999) 누적된 파일은 병합 대상에서 제외
                                    if (파일오류카운트.TryGetValue(원본파일경로, out int errCnt) && errCnt >= 999)
                                        continue;


                                    // =========================================================================
                                    // ★ [2차 방어] 파일이 완전히 작성되었는지(Lock 해제 및 0바이트 아님) 최종 확인
                                    // 특히 수십 MB 대용량 파일이 2초 넘게 써지고 있을 때 여기서 완벽히 방어합니다.
                                    // =========================================================================
                                    if (!IsFileReady(원본파일경로))
                                    {
                                        int 현재카운트 = 파일오류카운트.ContainsKey(원본파일경로) ? 파일오류카운트[원본파일경로] : 0;
                                        현재카운트++;
                                        if (현재카운트 >= 2) 현재카운트 = 999;
                                        파일오류카운트[원본파일경로] = 현재카운트;

                                        로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일Lock 대기] 대용량 작성 중이거나 대기 상태 : {1}", DateTime.Now, new FileInfo(원본파일경로).Name);
                                        로그기록(로그내용, 1);

                                        // 이동 및 읽기를 시도하지 않고 다음 파일로 안전하게 넘어갑니다.
                                        continue;
                                    }


                                        // 2. 파일 소유권 획득 시도 (완료 폴더로 선 이동)
                                    string 대상경로 = string.Format("{0}{1}_{2}", 이동폴더, PK중복방지, new FileInfo(원본파일경로).Name);
                                    bool 복사여부 = 복사_이동_삭제("복사_삭제", 원본파일경로, 대상경로);

                                    if (복사여부 == true)
                                    {
                                        // 3. 이동(삭제) 성공! 이제 완벽히 내 파일이므로 안전하게 읽습니다.
                                        try
                                        {
                                            string[] 파일읽기 = File.ReadAllLines(대상경로, Encoding.GetEncoding(949));
                                            출력내용.AddRange(파일읽기);
                                            안전하게이동된파일목록.Add(대상경로);

                                            // 성공했으니 에러 카운트 초기화
                                            파일오류카운트.Remove(원본파일경로);

                                            파일cnt++;
                                            로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일확보] 병합 대상 성공 : {1}", DateTime.Now, new FileInfo(원본파일경로).Name);
                                            로그기록(로그내용, 1);
                                        }
                                        catch (Exception)
                                        {
                                            // (이동은 했는데 읽기 실패하는 아주 예외적인 케이스)
                                            로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[내부오류] 파일 이동 후 읽기 실패 : {1}", DateTime.Now, 대상경로);
                                            로그기록(로그내용, 1);
                                        }
                                    }
                                    else
                                    {
                                        // 4. 이동(삭제) 실패 = 파일에 Lock이 걸려있음!
                                        // 절대 읽지 않고, 에러 카운트만 증가시킨 뒤 다음 파일로 넘어감
                                        int 현재카운트 = 파일오류카운트.ContainsKey(원본파일경로) ? 파일오류카운트[원본파일경로] : 0;
                                        현재카운트++;
                                        if (현재카운트 >= 3) 현재카운트 = 999;
                                        파일오류카운트[원본파일경로] = 현재카운트;

                                        로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일Lock] 병합 이동 실패, 재시도({1}/3) 대기 : {2}", DateTime.Now, 현재카운트 == 999 ? 3 : 현재카운트, new FileInfo(원본파일경로).Name);
                                        로그기록(로그내용, 1);
                                    }
                                }

                                // =========================================================
                                // [STEP 2] 최종 병합 파일 생성 (확보에 성공한 파일이 1개라도 있을 때만)
                                // =========================================================
                                if (안전하게이동된파일목록.Count > 0)
                                {
                                    이동파일 = 이동폴더 + string.Format("{0}_{1}.Re", PK중복방지, 처리파일);
                                    File.WriteAllLines(이동파일, 출력내용, Encoding.GetEncoding(949));

                                    로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일병합] ListFile병합완료 : {1:000} - {2} ", DateTime.Now, 파일cnt, new FileInfo(이동파일).Name);
                                    로그기록(로그내용, 1);
                                }
                                else
                                {
                                    // 💡 확보한 파일이 하나도 없다면 (전부 Lock이거나 999 에러 상태)
                                    이동파일 = "";
                                }
                            }

                            // =========================================================
                            // [중요 방어벽] 위 작업에서 모두 실패하여 이동파일이 없는 경우 하위 로직(읽기/분배) 스킵
                            // =========================================================
                            if (string.IsNullOrEmpty(이동파일))
                            {
                                // 현재 루프의 아래 로직(상태.입력폴더 할당, 파일 Split 등)을 건너뛰고
                                // 다음 루프에서 파일 Lock이 풀리기를 기다립니다.
                                i1_작업완료 = false; // 락 해제 (매우 중요)
                                continue;
                            }

                            상태.입력폴더 = 이동파일; // d:\Easy약관Plus\Input\@완료\#KB_250726_103620\250726_103620_#KB_Lngtrm.dat.Re
                            로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일이동] ListFil이동 :  {1} 이동", DateTime.Now, 이동파일);
                            로그기록(로그내용, 1);


                            string 분리_파일명 = System.IO.Path.GetFileNameWithoutExtension(상태.입력폴더);
                            string 분리_경로 = $"{System.IO.Path.GetDirectoryName(상태.입력폴더)}\\";


                            string[] lines = File.ReadAllLines(상태.입력폴더, Encoding.GetEncoding(949));
                            List<string> uniqueLines = new List<string>(lines.Length);
                            // 고객번호 저장용 HashSet (중복 체크용)
                            HashSet<string> seen = new HashSet<string>();
                            List<string> 중복data = new List<string>();
                            foreach (var line in lines) {
                                if (string.IsNullOrWhiteSpace(line))
                                    continue;
                                string[] 분리 = line.Split('|');
                                if (분리.Length == 0)
                                    continue;

                                string 고객번호 = 분리[0].Trim();
                                if (seen.Add(고객번호))
                                    uniqueLines.Add(line);
                                else
                                    중복data.Add(line);
                            }
                            if (중복data.Count > 0)
                            {
                                string 중복파일명 = $"{분리_경로}[중복]{분리_파일명}";
                                File.WriteAllLines(중복파일명, 중복data);
                            }

                            if (chk싱글처리.Checked == false)
                            {
                                // data, data처리 Table 내용 삭제
                                string SqlStr = string.Empty;
                                SqlStr = "DELETE FROM data처리";
                                await DBUpdate(SqlStr, 상태.dataTable접속);

                                SqlStr = "DELETE FROM data";
                                await DBUpdate(SqlStr, 상태.dataTable접속);
                            

                                int 병렬컴cnt = 1;
                                switch (저장.사용컴퓨터.ToLower())
                                {
                                    case "Kmain" :
                                        병렬컴cnt = 6;
                                        break;
                                    case "main":
                                    case "dzmain":
                                        병렬컴cnt = 3;
                                        break;
                                }

                                int total = uniqueLines.Count;
                                int n = total <= 병렬컴cnt ? total : tmp병렬cnt + 1 ; // 나눌 개수 ( + 1 : main )
                                int 몫 = total / n;
                                int 나머지 = total % n;
                                string 병렬처리문구 = $"[M{n:00}]";  // 병렬처리중 1건 이하는 싱글처리, 아니면 멀티처리(병렬)
                                int index = 0;

                                상태.병렬컴 = n;

                                for (int ii = 0; ii < n; ii++ )
                                {
                                    int 사이즈 = 몫 + (ii < 나머지 ? 1 : 0);

                                    // ✅ 배열 복사
                                    //string[] part = new string[사이즈];
                                    //Array.Copy(lines, index, part, 0, 사이즈);
                                    List<string> part = uniqueLines.GetRange(index, 사이즈);

                                    index += 사이즈;

                                    string 컴구분 = (ii == 0) ? "main" : $"{(string)lbxClients.Items[ii - 1]}"; //main, sub1 ~ sub5
                                    string savePath = $"{분리_경로}{병렬처리문구}{분리_파일명}.{컴구분}";
                                    File.WriteAllLines(savePath, part, Encoding.GetEncoding(949));
                                    await Task.Delay(TimeSpan.FromSeconds(1));

                                    if(컴구분 == "main")
                                    {
                                        bool 복사여부 = 복사_이동_삭제("복사_삭제", savePath, $"{저장.기본입력폴더}{병렬처리문구}{분리_파일명}.{컴구분}");
                                    } else
                                    {
                                        //// ★ [추가] 파일을 받은 서브 컴의 이름을 명단에 기록해둡니다.
                                        상태.작업참여클라이언트.Add(컴구분);
                                        int 접속확인 = lbxClients.Items.IndexOf(컴구분);
                                        if (접속확인 == -1)
                                        {
                                            로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[전송오류] {1}컴으로 {2}/{3} LIST파일전송실패", DateTime.Now, 컴구분, ii + 1, 상태.병렬컴);
                                            로그기록(로그내용, 1);
                                            continue;
                                        }

                                        await _server.서버To클라이언트송신(컴구분, new ChatHub
                                        {
                                            SendType = SendType.ServerToOneFile,
                                            State = ChatState.FileStart,
                                            FileFrom = 분리_경로,
                                            플레그 = "S To C : LIST파일전송",
                                            FileFilter = $"*.{컴구분}",
                                            UserName = 상태.상태컴퓨터,
                                            RoomId = 0
                                        });

                                        lbJobs.Items.Add(컴구분);
                                        작업상태아이콘업데이트();

                                        await Task.Delay(TimeSpan.FromSeconds (0.2));

                                        if (접속컴.TryGetValue(컴구분, out int ID))
                                        {
                                            로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[전송완료] {1}컴으로 {2}/{3} LIST파일전송 : {4:00000} Line", DateTime.Now, 컴구분, ii + 1, 상태.병렬컴, 사이즈);
                                            로그기록(로그내용, 1, ID);
                                        }
                                    }
                                }//END for

                            }
                            else if (chk싱글처리.Checked == true && 분산메인컴)
                            {
                                string tmpInputPath = $@"{저장.기본출력폴더}감시\TmpInput\";
                                if (!Directory.Exists(tmpInputPath)) Directory.CreateDirectory(tmpInputPath);

                                // ====================================================================
                                // 1. 놀고 있는 서브컴 찾기 (디스크 읽기 대신 lbJobs 메모리 검사로 속도 향상)
                                // ====================================================================
                                List<string> 대기중인서브컴 = new List<string>();
                                for (int c = 0; c < lbxClients.Items.Count; c++)
                                {
                                    string 컴구분 = lbxClients.Items[c].ToString();
                                    if (!lbJobs.Items.Contains(컴구분)) // 작업중 목록에 없으면 대기중
                                    {
                                        대기중인서브컴.Add(컴구분);
                                    }
                                }

                                int 놀고있는컴수 = 대기중인서브컴.Count;
                                int total = uniqueLines.Count;
                                int index = 0;

                                // ====================================================================
                                // 2. 스마트 분배 계획 수립 (데이터 총량에 따른 동적 분할 로직)
                                // ====================================================================
                                List<int> 할당계획 = new List<int>();

                                // [핵심] 총 건수(total)를 대기 중인 서브컴 수로 나누어 1대당 평균 감당량을 계산합니다.
                                int 할당단위 = 16; // 기본값 (최대치)

                                if (놀고있는컴수 > 0)
                                {
                                    // 소수점이 나올 수 있으므로 Math.Ceiling으로 올림 처리하여 보수적으로 잡습니다.
                                    int 평균할당량 = (int)Math.Ceiling((double)total / 놀고있는컴수);

                                    if (평균할당량 <= 4) 할당단위 = 4;
                                    else if (평균할당량 <= 8) 할당단위 = 8;
                                    else if (평균할당량 <= 12) 할당단위 = 12;
                                    else 할당단위 = 16; // 1대가 감당하기 벅찬 양이어도 최대 16건으로 제한
                                }
                                else
                                {
                                    할당단위 = 0; // 에러 방지: 놀고 있는 컴이 없으면 할당하지 않음
                                }

                                int 계획용_남은데이터 = total;

                                // 놀고 있는 컴퓨터 수만큼 반복하며 할당량을 나눠줍니다.
                                for (int ii = 0; ii < 놀고있는컴수; ii++)
                                {
                                    if (계획용_남은데이터 > 0)
                                    {
                                        // 남은 데이터가 할당단위보다 적으면 남은 것만, 많으면 할당단위 꽉 채워서 부여
                                        int 이번할당량 = Math.Min(할당단위, 계획용_남은데이터);
                                        할당계획.Add(이번할당량);
                                        계획용_남은데이터 -= 이번할당량;
                                    }
                                }

                                // ====================================================================
                                // 3. 분배 실행 (계획된 수량만큼 파일 생성 및 소켓 전송)
                                // ====================================================================
                                for (int ii = 0; ii < 할당계획.Count; ii++)
                                {
                                    string 컴구분 = 대기중인서브컴[ii];
                                    int 할당사이즈 = 할당계획[ii];

                                    // ====================================================================
                                    //  할당 사이즈에 따른 유동적 타임아웃을 계산하여 딕셔너리에 저장
                                    // ====================================================================
                                    int 타임아웃분 = 30; // 기본값 

                                    // ★ [변경] 이 부분을 위와 동일한 기준으로 덮어쓰세요!
                                    if (할당사이즈 <= 4) 타임아웃분 = 15;
                                    else if (할당사이즈 <= 8) 타임아웃분 = 20;
                                    else if (할당사이즈 <= 12) 타임아웃분 = 25;
                                    else 타임아웃분 = 30;

                                    서브컴할당건수[컴구분] = 할당사이즈;
                                    서브컴할당타임아웃[컴구분] = 타임아웃분;
                                    서브컴작업시작시간[컴구분] = DateTime.Now;

                                    // 데이터 자르기
                                    List<string> part = uniqueLines.GetRange(index, 할당사이즈);
                                    index += 할당사이즈;

                                    string 병렬처리문구 = $"[M{할당사이즈:00}]";
                                    string savePath = $"{분리_경로}{병렬처리문구}{분리_파일명}.{컴구분}";

                                    // 서브컴이 읽을 파일 생성
                                    File.WriteAllLines(savePath, part, Encoding.GetEncoding(949));
                                    await Task.Delay(TimeSpan.FromSeconds(0.2));

                                    // ★ [핵심] TmpInput 폴더에 복사본 남기기 (에러 대비 인질/백업)
                                    string tmpSavePath = $@"{tmpInputPath}{병렬처리문구}{분리_파일명}.{컴구분}";
                                    File.Copy(savePath, tmpSavePath, true);

                                    // 소켓 전송 (작업 지시)
                                    상태.작업참여클라이언트.Add(컴구분);
                                    await _server.서버To클라이언트송신(컴구분, new ChatHub
                                    {
                                        SendType = SendType.ServerToOneFile,
                                        State = ChatState.FileStart,
                                        FileFrom = 분리_경로,
                                        플레그 = "S To C : LIST파일전송",
                                        FileFilter = $"*.{컴구분}",
                                        UserName = 상태.상태컴퓨터,
                                        RoomId = 0
                                    });

                                    서브컴작업시작시간[컴구분] = DateTime.Now;

                                    // 서버 화면에 '작업중' 표시
                                    lbJobs.Items.Add(컴구분);
                                    작업상태아이콘업데이트();

                                    lbxClients.Items.Remove(컴구분); // 작업순서 순환구조
                                    lbxClients.Items.Add(컴구분);

                                    await Task.Delay(TimeSpan.FromSeconds(0.2));

                                    // 로그 기록
                                    if (접속컴.TryGetValue(컴구분, out int ID))
                                    {
                                        로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[파일전송] {1}컴 : {2:00}건 전송 완료", DateTime.Now, 컴구분, 할당사이즈);
                                        로그기록(로그내용, 1, ID);
                                        로그기록(로그내용, 0);

                                        // =========================================================
                                        // ★ 3. [추가] 파일 전송 액션(깜빡임) 효과 발동!
                                        // =========================================================
                                        리스트박스깜빡임효과(ID);
                                    }
                                }

                                // ====================================================================
                                // 4. 남은 찌꺼기 반환 (잔여대기 파일)
                                // 놀고 있는 서브컴이 없거나, 다 나눠주고도 데이터가 남았을 때 1번만 뱉어냄
                                // ====================================================================
                                if (index < total)
                                {
                                    List<string> 남은데이터 = uniqueLines.GetRange(index, total - index);
                                    string 잔여파일명 = $"{저장.기본파일폴더}#{상태.고객사}_잔여대기_{DateTime.Now:HHmmssFFFF}.lst";

                                    File.WriteAllLines(잔여파일명, 남은데이터, Encoding.GetEncoding(949));

                                    로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[관제탑대기] {1}건 잔여 데이터 반환 (서브컴 대기)", DateTime.Now, 남은데이터.Count);
                                    로그기록(로그내용, 1);
                                }
                                try
                                {
                                    if (File.Exists(상태.입력폴더)) // 상태.입력폴더가 바로 현재 읽은 .Re 파일입니다.
                                    {
                                        File.Delete(상태.입력폴더);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[파일삭제오류] 원본 파일 삭제 실패: {ex.Message}", 1);
                                }
                            }


                            실행여부 = "파일분리";
                        } //END if

                    } //END for

                    if (실행여부 == "단계01")
                    {
                        실행여부 = "성공";
                        break;
                    }
                    else if (실행여부 == "파일분리" && chk싱글처리.Checked && 분산메인컴)
                    {
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                catch (Exception ex)
                {
                    재시도++;
                    if (재시도 <= 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        continue;
                    }
                    실행여부 = "실패";
                    로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[단계01파일검색_파일분리] 오류 : {1} - {2}", DateTime.Now, ex.Message, ex.StackTrace);
                    로그기록(로그내용, 1);
                    

                    int listIndex = -1;
                    if (실행주체 == "클라이언트")
                    {
                        listIndex = 9;
                        _clientHandler?.Send(new ChatHub
                        {
                            RoomId = C_RoomId,
                            UserName = C_UserName,
                            Message = $"[작업오류] {ex.Message}",
                            상황 = "오류",
                            State = ChatState.진행상황
                        });
                    } else
                    {
                        listIndex = 0;
                    }
                    SetError(true, listIndex, "ERROR");
                    break;
                }
            }
            return 실행여부;
        }
        private bool IsFileReady(string filePath)
        {
            try
            {
                // 파일을 독점(FileShare.None)으로 열어봅니다.
                // 송신 측 프로그램이 아직 파일을 쓰는 중이라면 여기서 무조건 예외(IOException)가 터집니다.
                using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    // 0바이트인 빈 껍데기 파일도 아직 안 써진 것으로 간주하여 방어합니다.
                    return fs.Length > 0;
                }
            }
            catch (IOException)
            {
                // 누군가 파일을 쓰고 있거나 잠겨 있음 (정상적인 보호 작동)
                return false;
            }
        }
        private void 리스트박스깜빡임효과(int listIndex)
        {
            if (listIndex < 0 || listIndex >= 리스트.Length || 리스트[listIndex] == null) return;

            Task.Run(async () =>
            {
                try
                {
                    var targetList = 리스트[listIndex];

                    for (int i = 0; i < 3; i++)
                    {
                        this.Invoke(new Action(() =>
                        {
                            // ★ 1. 깜빡임 상태 ON
                            리스트깜빡임상태[listIndex] = true;

                            targetList.Appearance.BackColor = Color.LightSkyBlue;
                            targetList.Appearance.Options.UseBackColor = true;
                            targetList.Refresh(); // Invalidate보다 강력하게 모든 아이템을 즉시 다시 그리도록 강제함
                        }));

                        await Task.Delay(150);

                        this.Invoke(new Action(() =>
                        {
                            // ★ 2. 깜빡임 상태 OFF
                            리스트깜빡임상태[listIndex] = false;

                            targetList.Appearance.Options.UseBackColor = false;
                            targetList.Refresh();
                        }));

                        await Task.Delay(150);
                    }
                }
                catch { }
            });
        }


        // ==========================================================================================================
        // 02 - 02 data처리 Table를 검색해서 데이터를 처리한다.
        // ==========================================================================================================
        private async Task<bool> data처리Table검색(현재상태 상태, string 작업선택)
        {
            int 재시도 = 0;
            bool 실행여부 = false;
            while (true)
            {
                try
                {
                    string SqlStr = "";
                    string 구분문구 = "";
                    if (작업선택 == "HWP2PDF")
                    {
                        구분문구 = "PDF변환작업 <> 'PDF변환완료'";
                    }
                    else if (작업선택 == "PDFMERGE") {

                        구분문구 = "PDF병합작업 <> 'PDF병합완료'";
                    }
                    else {
                        break;
                    }

                    SqlStr = $@"
                            SELECT * 
                            FROM data처리 
                            WHERE groupKey = '{상태.groupKey}' AND
                                  처리구분 = '{상태.처리구분}' AND
                                  {구분문구}
                    ";

                    using (SqlConnection AdoNet = new SqlConnection(상태.dataTable접속))
                    {
                        await AdoNet.OpenAsync();
                        DataSet ds = new DataSet();
                        DataTable dataTable = new DataTable();
                        SqlCommand sqlcmd = new SqlCommand(SqlStr, AdoNet);
                        sqlcmd.CommandTimeout = 0;
                        SqlDataAdapter adapter = new SqlDataAdapter(sqlcmd);
                        adapter.Fill(ds, "기본");

                        if (ds.Tables["기본"].Rows.Count == 0)
                        {
                            string fallbackSql = $"SELECT TOP 1 * FROM data처리 WHERE groupKey = '{상태.groupKey}'";
                            using (SqlCommand fbCmd = new SqlCommand(fallbackSql, AdoNet))
                            {
                                SqlDataAdapter fbAdapter = new SqlDataAdapter(fbCmd);
                                DataSet fbDs = new DataSet();
                                fbAdapter.Fill(fbDs, "복구");

                                if (fbDs.Tables["복구"].Rows.Count > 0)
                                {
                                    DataRow fbRow = fbDs.Tables["복구"].Rows[0];
                                    DB2구조체Save(상태, fbRow); // 여기서 상태.병렬컴 등에 원래 값이 쏙 들어갑니다!

                                    // this.Invoke나 로그기록 형식에 맞춰 작성 (필요시)
                                    // 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[상태복구] 0건 스킵 중 상태변수 정상 복구", 1);
                                }
                            }

                            return true;
                        }
                        DataRow row = ds.Tables["기본"].Rows[0];
                        DB2구조체Save(상태, row);
                        실행여부 = true;
                    }
                    break;
                }
                catch (Exception ex)
                {

                    재시도++;
                    if (재시도 <= 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        continue;
                    }
                    실행여부 = false;
                    로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[data처리Table검색] 오류 : {1} - {2}", DateTime.Now, ex.Message, ex.StackTrace);
                    로그기록(로그내용, 1);


                    int listIndex = -1;
                    if (실행주체 == "클라이언트")
                    {
                        listIndex = 9;
                        _clientHandler?.Send(new ChatHub
                        {
                            RoomId = C_RoomId,
                            UserName = C_UserName,
                            Message = $"[작업오류] {ex.Message}",
                            상황 = "오류",
                            State = ChatState.진행상황
                        });
                    }
                    else
                    {
                        listIndex = 0;
                    }
                    SetError(true, listIndex, "ERROR");
                    break;
                }
            }
            return 실행여부;
        }

        // ==========================================================================================================
        // 02 - 2 : DB to 구조체 Save
        // ==========================================================================================================
        private void DB2구조체Save<T>(T target, DataRow row)
        {
            var props = typeof(T).GetProperties();

            foreach (var prop in props)
            {
                if (row.Table.Columns.Contains(prop.Name) && prop.CanWrite)
                {
                    var value = row[prop.Name];
                    if (value != DBNull.Value)
                    {
                        prop.SetValue(target, Convert.ChangeType(value, prop.PropertyType));
                    }
                }
            }
        }

        // ==========================================================================================================
        // 03 - 01 : F1 ~ F8 Hwp to PDF 변환 앱을 실행한다.
        //           8개의 앱이 실행되면 최대 10번까지 오류처리를 한다.
        // ==========================================================================================================
        private async Task<bool> F1실행관리(현재상태 상태, CancellationToken token = default)
        {
            // 중복 실행 차단
            if (Interlocked.Exchange(ref 실행상태, 1) == 1)
            {
                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[제어앱] 이미 감시 루프 실행 중";
                로그기록(로그내용, 1);
                return false;
            }

            bool 실행여부 = false;
            int 앱수 = 상태.앱실행수; // 최대 8            
            using (var cts = new CancellationTokenSource()) 
            {
                try
                {
                    List<Task<bool>> 감시작업목록 = new List<Task<bool>>();
                    // 앱별 감시 시작
                    for (int i = 0; i < 앱수; i++)
                    {
                        int index = i + 1;
                        감시작업목록.Add(Task.Run(() => F앱감시루프(상태, index, cts.Token)));
                    }

                    // 모든 앱이 정상완료될 때까지 대기
                    bool[] 결과 = await Task.WhenAll(감시작업목록);
                    
                    // 약관완료 감시 (모든 F앱이 *.약관완료 파일을 생성해야 함)
                    bool 전체완료 = await 모든약관완료감시(상태.groupKey, 앱수);

                    string msg = 전체완료 && 결과.All(r => r)
                        ? $"{DateTime.Now:yy.MM.dd HH:mm:ss}[제어앱] 모든 F앱 변환 완료 → PDF 병합 시작"
                        : $"{DateTime.Now:yy.MM.dd HH:mm:ss}[제어앱] 일부 F앱 변환 미완료 또는 오류 발생";

                    this.BeginInvoke(new Action(() => 로그기록(msg, 1)));
                    실행여부 = 전체완료 && 결과.All(r => r);
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[제어앱] 감시관리 오류: {ex.Message}", 1)));
                }
                finally
                {
                    cts.Cancel();
                    Interlocked.Exchange(ref 실행상태, 0);  // 실행 완료 → 0으로 복귀
                }

            }
            return 실행여부;
        }

        // =========================================================================
        // ★ [추가] 한컴오피스 좀비 싹쓸이 프로시저 (0.1초 컷)
        // =========================================================================
        private void Hwp프로세스싹쓸이()
        {
            try
            {
                // 32비트 한컴오피스의 프로세스 기본 이름은 "Hwp" (또는 "Hwp.exe") 입니다.
                Process[] 좀비들 = Process.GetProcessesByName("Hwp");

                if (좀비들.Length > 0)
                {
                    foreach (Process p in 좀비들)
                    {
                        try
                        {
                            if (!p.HasExited)
                            {
                                p.Kill(); // 자비 없이 즉각 사살
                            }
                        }
                        catch { } // 권한 문제 등으로 못 죽이는 건 가볍게 무시 (시스템 다운 방지)
                    }

                    // (선택) 몇 마리의 좀비를 잡았는지 로그로 확인하고 싶으시면 아래 주석을 푸세요.
                    // string 로그 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[좀비청소] 잔류 HWP 프로세스 {좀비들.Length}개 강제 종료 완료";
                    // this.Invoke(new Action(() => 로그기록(로그, 1)));
                }
            }
            catch { }
        }



        // ✅ 모든 약관완료 감시
        private async Task<bool> 모든약관완료감시(string tmpgroupKey, int 앱수)
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start) < TimeSpan.FromMinutes(5))
            {
                int 완료파일수 = 0;
                for (int i = 1; i <= 앱수; i++)
                {
                    string H2P완료파일 = $"{저장.기본출력폴더}감시\\{tmpgroupKey}.F{i}_종료";
                    if (new FileInfo (H2P완료파일).Exists)
                        완료파일수++;
                }

                if (완료파일수 >= 앱수)
                    return true;

                await Task.Delay(2000);
            }
            return false;
        }

        private int[] 앱별연속실패 = new int[9];
        // [주의] 이 변수는 반드시 메서드 밖(클래스 전역)에 선언되어 있어야 합니다!
        // private int[] 앱별연속실패 = new int[9]; 

        private async Task<bool> F앱감시루프(현재상태 상태, int index, CancellationToken token)
        {
            string 실행경로 = $@"c:\Easy약관Plus\program\H2P\F{index}\";
            string exePath = $"{실행경로}f1.exe";
            string 프로세스이름 = System.IO.Path.GetFileNameWithoutExtension(exePath);
            string 완료경로 = $"{상태.출력폴더}{index}\\@완료\\";

            string H2P완료파일 = $"{저장.기본출력폴더}감시\\{상태.groupKey}.F{index}_종료";
            string 작업경로 = $"{상태.출력폴더}{index}\\";
            string 약관완료파일 = $"{상태.출력폴더}{index}\\F{index}.약관완료";

            if (!Directory.Exists(완료경로))
                Directory.CreateDirectory(완료경로);

            // 초기 상태는 대기(False)
            체크박스상태업데이트(index, false);
            UpdateFProcessState(index - 1, "WAITING");

            bool 정상완료 = false;
            int 실행횟수 = 0;
            DateTime lastCheck;
            while (!token.IsCancellationRequested)
            {
                // 누군가 서킷 브레이커를 발동시켜 전체 초기화 중이라면 대기
                if (isRestarting || F앱전체초기화중)
                {
                    await Task.Delay(2000, token);
                    continue;
                }

                try
                {
                    // 1️⃣ 프로세스 존재 여부 확인 및 실행
                    Process proc = null;
                    if (첫실행)
                    {
                        await F1프로그램종료(index);
                        첫실행 = false;
                    }
                    else
                    {
                        proc = Process.GetProcessesByName(프로세스이름)
                            .FirstOrDefault(p =>
                            {
                                string currentPath = GetProcessPathByApi(p.Id);
                                return string.Equals(currentPath, exePath, StringComparison.OrdinalIgnoreCase);
                            });
                    }

                    if (proc != null && !proc.HasExited)
                    {
                        // 정상 동작 중
                        체크박스상태업데이트(index, true);
                        UpdateFProcessState(index - 1, "RUNNING");
                    }
                    else
                    {
                        // 없으면 즉시 재실행
                        await F1프로그램종료(index);
                        await Task.Delay(500);

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = 실행경로,
                            Arguments = 파라메타,
                            UseShellExecute = true,
                        };

                        proc = Process.Start(startInfo);
                        체크박스상태업데이트(index, true);
                        UpdateFProcessState(index - 1, "RUNNING");

                        if (proc != null)
                        {
                            try { proc.WaitForInputIdle(5000); } catch { }
                        }
                        else
                        {
                            this.Invoke(new Action(() => {
                                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[F{index}] 프로세스 시작 실패 (proc null)", 1);
                            }));
                            await Task.Delay(2000, token);
                            continue;
                        }

                        lastCheck = DateTime.Now;
                    }

                    // 2️⃣ 플래그 및 성과(진행도) 감시
                    lastCheck = DateTime.Now;
                    int lastFileCount = Directory.GetFiles(완료경로, "*.hwp").Length;

                    while (!proc.HasExited && !token.IsCancellationRequested)
                    {
                        await Task.Delay(3000, token);

                        // =======================================================================
                        //  H2P(감시자) 앱도 물리적 개수가 아닌 논리적 개수로 판단!
                        // =======================================================================
                        FileInfo[] 현재작업파일들 = new DirectoryInfo(작업경로).GetFiles("Tmp_*.hwp");
                        HashSet<string> 완료된파일들 = new HashSet<string>(
                            new DirectoryInfo(완료경로).GetFiles("Tmp_*.hwp").Select(f => f.Name),
                            StringComparer.OrdinalIgnoreCase);

                        // 완료폴더에 없는 '진짜' 대기 파일만 추출
                        var 진짜대기파일들 = 현재작업파일들.Where(f => !완료된파일들.Contains(f.Name)).ToList();

                        // 물리적 파일 개수 대신 이 논리적 개수를 기준으로 모든 것을 판단합니다.
                        int 진짜대기중인HWP = 진짜대기파일들.Count;
                        // =======================================================================

                        bool H2P종료 = new FileInfo(H2P완료파일).Exists;

                        // 기존 hwpCount == 0 대신 진짜대기중인HWP == 0 사용
                        정상완료 = (H2P종료 && 진짜대기중인HWP == 0);

                        if (정상완료)
                        {
                            UpdateFProcessState(index - 1, "WAITING");
                            체크박스상태업데이트(index, false);
                            앱별연속실패[index] = 0; // 작업 완수했으므로 카운트 리셋
                            return 정상완료;
                        }

                        int currentCount = Directory.GetFiles(완료경로, "*.hwp").Length;
                        if (currentCount != lastFileCount)
                        {
                            // ★ 성과 발생! 카운트 즉시 리셋
                            lastFileCount = currentCount;
                            lastCheck = DateTime.Now;
                            앱별연속실패[index] = 0;
                        }
                        else
                        {
                            //bool 약관완료됨 = new FileInfo(약관완료파일).Exists;
                            TimeSpan 허용대기시간 = TimeSpan.FromSeconds(120); // 기본 최소 대기시간 보장

                            // 진짜대기중인HWP(완료 제외) > 0 일 때
                            if (진짜대기중인HWP > 0)
                            {
                                // 무조건 가장 먼저 생성된(LastWriteTime) 파일을 기준으로 타겟팅!
                                FileInfo 현재처리예상파일 = 진짜대기파일들.OrderBy(f => f.LastWriteTime).ThenBy(f => f.Name).First();

                                // F1 앱과 완전히 똑같은 방식으로 용량 기반 시간 계산
                                double fileSizeKB = 현재처리예상파일.Length / 1024.0;
                                int chunks = (int)Math.Ceiling(fileSizeKB / 500.0);
                                if (chunks == 0) chunks = 1;

                                // F1 기본 1회차 타임아웃 (+300초 보장)
                                int baseTimeout = chunks * 40 + 300;

                                // ★ [원복] F1이 조건에 따라 2회차를 시도할 경우를 대비해, H2P는 무조건 2회차 최대 시간을 기다려 줌
                                int h2p최종타임아웃 = baseTimeout * 2 + 30 + 10;

                                허용대기시간 = TimeSpan.FromSeconds(h2p최종타임아웃);

                                // 관제탑 시계에 가장 긴 대기 시간 보고
                                if (h2p최종타임아웃 > 글로벌동적타임아웃)
                                {
                                    글로벌동적타임아웃 = h2p최종타임아웃;
                                }
                            }

                            // =========================================================================
                            // ★ [추가 2] 나 혼자 멈춘 게 아니라, 한컴 엔진 전체(공용 시계)가 멈췄을 때만 사살!
                            // =========================================================================
                            if ((DateTime.Now - lastCheck) > 허용대기시간)
                            {
                                앱별연속실패[index]++; // 성과 없이 에러 났으므로 실패 카운트 증가

                                try
                                {
                                    UpdateFProcessState(index - 1, "ERROR");
                                    SafeKillProcess(proc, 프로세스이름); // ★ 일반 Kill 대신 강제 사살 헬퍼 사용
                                    await F1프로그램종료(index);
                                    실행횟수++;
                                    this.Invoke(new Action(() =>
                                    {
                                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[F{index}] 앱 멈춤(Freeze) 감지 → 프로세스 강제 재시작 (연속실패: {앱별연속실패[index]}회)", 1);
                                    }));

                                    // =========================================================================
                                    // ★ [추가 3] 고장 난 앞 차를 치웠으니, 억울하게 대기 중이던 다른 앱들에게 유예 시간을 줍니다!
                                    // =========================================================================
                                }
                                catch { }

                                // =========================================================================
                                // 마비된 앱 개수 확인 후 서킷 브레이커(전체 초기화) 발동
                                // =========================================================================
                                int 마비된앱수 = 앱별연속실패.Skip(1).Take(상태.앱실행수).Count(failCnt => failCnt >= 2);

                                if (마비된앱수 >= (상태.앱실행수 / 2) && !F앱전체초기화중) // 절반 이상 마비 시 작동
                                {
                                    bool triggered = false;
                                    lock (_circuitBreakerLock)
                                    {
                                        if (!F앱전체초기화중)
                                        {
                                            F앱전체초기화중 = true;
                                            triggered = true;
                                        }
                                    }

                                    if (triggered)
                                    {
                                        this.Invoke(new Action(() =>
                                        {
                                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[에러폭주차단] 절반 이상 마비 감지. 전체 초기화 진행!", 1);
                                        }));

                                        var killTasks = new List<Task>();
                                        for (int i = 1; i <= 상태.앱실행수; i++)
                                        {
                                            UpdateFProcessState(i - 1, "WAITING");
                                            killTasks.Add(F1프로그램종료(i));
                                        }
                                        await Task.WhenAll(killTasks);
                                        await Task.Delay(3000); // 시스템 자원 정리 시간 확보

                                        // 전체 초기화 후 배열 완전 리셋
                                        for (int i = 1; i <= 8; i++) 앱별연속실패[i] = 0;

                                        F앱전체초기화중 = false;

                                        this.Invoke(new Action(() =>
                                        {
                                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[초기화완료] 꼬인 메모리 제거 완료. 일제히 재출발합니다.", 1);
                                        }));
                                    }
                                }

                                break; // 루프를 빠져나가면 상단의 while 처음으로 돌아가 재실행
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() =>
                    {
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[F{index}] 감시 중 오류: {ex.Message}", 1);
                    }));
                }

                await Task.Delay(3000);
            }
            return 정상완료;
        }
        private void SafeKillProcess(Process p, string procName)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
            }
            catch (System.ComponentModel.Win32Exception) // 액세스 거부 시
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /PID {p.Id}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    })?.WaitForExit(1000);
                }
                catch { }
            }
            catch { }
        }

        private void 체크박스상태업데이트(int index, bool 실행중)
        {
            F앱상태[index] = 실행중;

            // 3. LINQ를 활용한 현재 상태 요약 (1초 모아서 한 번만 출력)
            lock (요약락)
            {
                if (!로그출력예약됨)
                {
                    로그출력예약됨 = true;

                    Task.Delay(1000).ContinueWith(t =>
                    {
                        lock (요약락)
                        {
                            // LINQ로 true(실행중) 갯수 카운트
                            int 실행수 = F앱상태.Count(x => x);
                            int 대기수 = 8 - 실행수;
                            if (대기수 == 0 || 대기수 == 8)
                            {
                                string 요약로그 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[F{index} 실행] 실행: {실행수} | 대기: {대기수}";
                                this.Invoke(new Action(() => 로그기록(요약로그, 1)));
                            }
                            로그출력예약됨 = false;
                        }
                    });
                }
            }
        }

        private bool FindOrKillProcessByPath(string processName, string exePath, string mode, string 앱인수 = "")
        {
            bool 찾기 = false;
            string procNameOnly = System.IO.Path.GetFileNameWithoutExtension(processName);

            try
            {
                Process[] processes = Process.GetProcessesByName(procNameOnly);

                foreach (Process p in processes)
                {
                    // ★ 중요: MainModule을 호출하지 않고 바로 API를 씁니다. (오류 원천 차단)
                    string currentPath = GetProcessPathByApi(p.Id);

                    if (!string.IsNullOrEmpty(currentPath) &&
                        string.Equals(currentPath, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        찾기 = true;

                        if (mode == "kill")
                        {
                            try
                            {
                                p.Kill();
                                p.WaitForExit(1000);
                                string 종료앱 = string.IsNullOrWhiteSpace(앱인수) ? processName : 앱인수;
                                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[앱종료] {종료앱}";
                                로그기록(로그내용, 1);
                            }
                            catch (Exception ex)
                            {
                                // 권한 문제로 종료 못 할 수도 있음
                                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[종료실패] {ex.Message}";
                                로그기록(로그내용, 1);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[프로세스조회오류] {ex.Message}";
                로그기록(로그내용, 1);
            }

            return 찾기;
        }


        private string GetProcessPathByApi(int pid)
        {
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                // 제한된 권한으로 열기 때문에 32비트 앱에서도 64비트 앱 정보를 볼 수 있음
                hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

                // 접근 권한이 아예 없는 시스템 프로세스(백신, 윈도우 커널 등)는 0을 반환함
                if (hProcess == IntPtr.Zero) return "";

                int capacity = 1024;
                StringBuilder sb = new StringBuilder(capacity);
                int size = capacity;

                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    return sb.ToString();
                }
            }
            catch
            {
                // 혹시 모를 API 오류 무시
            }
            finally
            {
                if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
            }
            return "";
        }

        // ==========================================================================================================
        // Easy약관Plus : 앱이 응답없음이면 재시작
        // ==========================================================================================================

        private async Task<bool> Easy약관Plus실행()
        {
            bool 실행여부 = true;
            const int 최대재시도 = 3;
            string 실행파일명 = "Easy약관Plus.exe";
            string 실행경로 = @"c:\Easy약관Plus\program\";
            //string 실행경로 = @"d:\Easy약관Plus\Easy약관PDF\H2PControl\bin\Debug\";

            string 실행파일전체경로 = System.IO.Path.Combine(실행경로, 실행파일명);
            string 프로세스이름 = System.IO.Path.GetFileNameWithoutExtension(실행파일명);

            try
            {

                // 🔹 앱실행여부  확인 (정확히 같은 경로의 프로세스만)
                bool 앱실행여부 = FindOrKillProcessByPath(프로세스이름, 실행파일전체경로, "check");
                if (앱실행여부)
                {
                    로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[Easy약관Plus APP종료] Easy약관Plus APP 종료 종료합니다.";
                    로그기록(로그내용, 1);
                    FindOrKillProcessByPath(프로세스이름, 실행파일전체경로, "kill");

                    // 약간의 대기 후 재실행 (완전 종료 보장)
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

                // 🔸 실행되지 않았으면 새로 실행
                for (int 시도 = 1; 시도 <= 최대재시도; 시도++)
                {
                    try
                    {
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = 실행파일전체경로,
                            WorkingDirectory = 실행경로,
                            //Arguments = $"\"{상태.groupKey}\"", // 따옴표로 감싸기
                            Arguments = "재시작",
                            UseShellExecute = true,
                            //WindowStyle = ProcessWindowStyle.Hidden  //CreateNoWindow = true
                        };

                        Process Easy약관Plus제어 = new Process { StartInfo = startInfo };
                        Easy약관Plus제어.Start();

                        로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[Easy약관Plus APP실행] Easy약관Plus APP 실행을 시작 합니다.";
                        로그기록(로그내용, 1);
                        break;
                    }
                    catch (Exception ex)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        실행여부 = false;

                        if (시도 == 최대재시도)
                        {
                            로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[ERROR] Easy약관Plus APP실행 오류 : {시도}번 모두 실행 실패 - {ex.Message}";
                            로그기록(로그내용, 1);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                실행여부 = false;
                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[ERROR] ◎진행불가 : Easy약관Plus APP실행 오류 - {ex.Message}";
                로그기록(로그내용, 1);
            }
            return 실행여부;
        }

        // ==========================================================================================================
        // 04 - 01 선택약관PDF병합 : 세션별로 되어 있는 PDF파일을 하나로 병합 하는 단계
        // ==========================================================================================================
        // ==========================================================================================================
        // 04 - 01 선택약관PDF병합 : 세션별로 되어 있는 PDF파일을 하나로 병합 하는 단계
        // ==========================================================================================================
        private async Task<bool> 선택약관PDF병합(현재상태 상태, CancellationToken token = default)
        {
            int 재시도 = 0;
            bool 실행여부 = false;
            while (true)
            {
                try
                {
                    string StateFile상태 = "";
                    int PDFcnt = new DirectoryInfo(상태.출력폴더).GetFiles("*.pdf").Count();
                    int XXXcnt = new DirectoryInfo(상태.출력폴더).GetFiles("*.xxx").Count();
                    if (PDFcnt == 0 && XXXcnt == 0)
                    {
                        StateFile상태 = string.Format(@"{0}감시\\", 저장.기본출력폴더);
                        int PDF변환완료Fcnt = new DirectoryInfo(StateFile상태).GetFiles($"{상태.groupKey}.*_종료").Count();

                        if (PDF변환완료Fcnt == 상태.앱실행수)
                        {
                            return true;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1));
                        continue;
                    }

                    var 완료고객번호 = new ConcurrentBag<string>();
                    var 오류고객번호 = new ConcurrentBag<string>();

                    // =========================================================================
                    // ★ [연쇄 격리 및 DB 오류 처리를 위한 사전 스캔]
                    // =========================================================================
                    HashSet<string> 현재루프오류PK = new HashSet<string>();
                    string 격리폴더 = System.IO.Path.Combine(상태.출력폴더, "@에러격리");
                    if (!Directory.Exists(격리폴더)) Directory.CreateDirectory(격리폴더);

                    foreach (FileInfo xxx파일 in new DirectoryInfo(상태.출력폴더).GetFiles("*.xxx"))
                    {
                        string[] tmp분리 = xxx파일.Name.Split('_').Select(P => P.Trim()).ToArray();

                        if (tmp분리.Length > 1)
                        {
                            int pk시작위치 = (상태.고객사 == "CH" && tmp분리[1] == "06") ? 3 : 2;
                            if (tmp분리.Length > pk시작위치)
                            {
                                string tmpPK = string.Join("_", tmp분리, 0, tmp분리.Length - pk시작위치);
                                if (현재루프오류PK.Add(tmpPK)) 오류고객번호.Add(tmpPK);
                            }
                        }
                        try
                        {
                            string 대상경로 = System.IO.Path.Combine(격리폴더, xxx파일.Name);
                            if (File.Exists(대상경로)) File.Delete(대상경로);
                            xxx파일.MoveTo(대상경로);
                        }
                        catch { }
                    }

                    if (현재루프오류PK.Count > 0)
                    {
                        상태.오류고객목록 = 현재루프오류PK.ToList();
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [오류고객감지] 총 {현재루프오류PK.Count}명 DB 오류 처리 및 병합 제외.", 1);
                    }

                    List<세션파일> 분할선택약관 = new List<세션파일>();

                    foreach (FileInfo 파일 in new DirectoryInfo(상태.출력폴더).GetFiles("*.pdf"))
                    {
                        string[] tmp분리 = 파일.Name.Split('_').Select(P => P.Trim()).ToArray();
                        int pk시작위치 = (상태.고객사 == "CH" && tmp분리[1] == "06") ? 3 : 2;
                        string tmpPK = string.Join("_", tmp분리, 0, tmp분리.Length - pk시작위치);

                        if (현재루프오류PK.Any(오류PK => 오류PK == tmpPK))
                        {
                            try
                            {
                                string 대상경로 = System.IO.Path.Combine(격리폴더, 파일.Name);
                                if (File.Exists(대상경로)) File.Delete(대상경로);
                                파일.MoveTo(대상경로);
                            }
                            catch { }
                            continue;
                        }

                        분할선택약관.Add(new 세션파일() { PK = tmpPK, 경로 = 파일.FullName });
                    }

                    HashSet<string> 중복고객 = new HashSet<string>();
                    var semaphore = new SemaphoreSlim(8);
                    var tasks = new List<Task>();

                    using (var reader = await LoadReaderAsync(상태).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            string 고객Key = reader["고객Key"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(고객Key)) continue;

                            if (현재루프오류PK.Any(오류PK => 오류PK == 고객Key))
                            {
                                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[격리제외] 고객번호 : {고객Key} (오류 발생으로 병합에서 제외됨)", 1);
                                continue;
                            }

                            string[] 분리 = reader["data"].ToString().Split('|').Select(P => P.Trim()).ToArray();

                            if (분리[3] == "*")
                            {
                                string tmp종분류코드 = (string.IsNullOrEmpty(분리[4])) ? "" : 분리[4];
                                Array.Resize(ref 분리, 18);
                                for (int idx = 4; idx <= 17; idx++) 분리[idx] = "";
                                분리[9] = tmp종분류코드;
                                분리[10] = "02";
                                분리[11] = "02";
                            }
                            if (분리[10] == "03" || 분리[10] == "3")
                            {
                                완료고객번호.Add(고객Key);
                                continue;
                            }

                            var 출력약관 = from 파일 in 분할선택약관
                                       where 파일.PK == reader["고객Key"].ToString()
                                       orderby 파일.경로 ascending
                                       select new { PK = 파일.PK, 경로 = 파일.경로 };

                            List<string> 만들파일 = new List<string>();
                            string 목차경로 = "";
                            foreach (var 약관 in 출력약관)
                            {
                                if (약관.경로.Contains("_IX_")) 목차경로 = 약관.경로;
                                else 만들파일.Add(약관.경로);
                            }

                            if (!string.IsNullOrWhiteSpace(목차경로)) 만들파일.Insert(1, 목차경로);

                            if (!중복고객.Add(고객Key))
                            {
                                로그기록(string.Format("{0:yy.MM.dd HH:mm:ss}[ER중복고객: {1}] ", DateTime.Now, reader["고객Key"].ToString()), 1);
                                continue;
                            }

                            if (출력약관.Count() == 0)
                            {
                                로그기록(string.Format("{0:yy.MM.dd HH:mm:ss}[약관병합오류] 고객번호 : {1} 약관이 존재 하지 않습니다.", DateTime.Now, reader["고객Key"].ToString()), 1);
                                오류고객번호.Add(고객Key);
                                continue;
                            }

                            string 출력파일 = string.Format("{0}Y\\Y{1}.pdf", 상태.출력폴더, reader["파일명"].ToString());
                            string 전체약관경로 = reader["전체약관경로"]?.ToString() ?? "";

                            tasks.Add(Task.Run(async () =>
                            {
                                await semaphore.WaitAsync().ConfigureAwait(false);
                                try
                                {
                                    bool ok = false;
                                    if (!string.IsNullOrWhiteSpace(전체약관경로))
                                        ok = true;
                                    else
                                    {
                                        // ★ [변경포인트] PdfManager 호출 (UI 종속성 제거)
                                        Action<string> logAction = (msg) =>
                                        {
                                            this.BeginInvoke(new Action(() => 로그기록(msg, 1)));
                                        };
                                        ok = await PdfManager.개별PDF세션결합(출력파일, 만들파일, 분리, 상태, logAction).ConfigureAwait(false);
                                    }

                                    if (ok)
                                    {
                                        완료고객번호.Add(고객Key);
                                        this.BeginInvoke(new Action(() =>
                                        {
                                            로그기록(string.Format("{0:yy.MM.dd HH:mm:ss}[선택약관PDF병합 성공] 고객번호 : {1}", DateTime.Now, 고객Key), 1);
                                        }));
                                    }
                                    else
                                    {
                                        오류고객번호.Add(고객Key);
                                        this.BeginInvoke(new Action(() =>
                                        {
                                            로그기록(string.Format("{0:yy.MM.dd HH:mm:ss}[선택약관PDF병합 실패] 고객번호 : {1}", DateTime.Now, 고객Key), 1);
                                        }));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    오류고객번호.Add(고객Key);
                                    this.BeginInvoke(new Action(() =>
                                    {
                                        로그기록(string.Format("{0:yy.MM.dd HH:mm:ss}[선택약관PDF병합 실패] 고객번호 : {1}, {2}", DateTime.Now, 고객Key, ex.Message), 1);
                                    }));
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));

                        }
                    }

                    // 모든 고객 병합 작업 완료 대기
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    await BatchUpdateAsync(상태, 완료고객번호, 오류고객번호).ConfigureAwait(false);

                    실행여부 = true;
                    break;
                }
                catch (Exception ex)
                {
                    재시도++;
                    if (재시도 <= 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        continue;
                    }
                    실행여부 = false;
                    로그기록(string.Format("{0:yy.MM.dd HH:mm:ss}[선택약관PDF병합] 오류 : {1} - {2}", DateTime.Now, ex.Message, ex.StackTrace), 1);

                    int listIndex = -1;
                    if (실행주체 == "클라이언트")
                    {
                        listIndex = 9;
                        _clientHandler?.Send(new ChatHub
                        {
                            RoomId = C_RoomId,
                            UserName = C_UserName,
                            Message = $"[작업오류] {ex.Message}",
                            상황 = "오류",
                            State = ChatState.진행상황
                        });
                    }
                    else
                    {
                        listIndex = 0;
                    }
                    SetError(true, listIndex, "ERROR");
                    break;
                }

            }

            return 실행여부;
        }

        // ==========================================================================================================
        // 04 - 02 data, data처리 테이블 Update : PDF병합완료/PDF병합오류
        //         data처리는 기본이 '대기'이며 오류가 발생하면 Update한다.
        //         100개씩 배치 업데이트
        // ==========================================================================================================
        private async Task BatchUpdateAsync(현재상태 상태, ConcurrentBag<string> 완료, ConcurrentBag<string> 오류)
        {
            const int 묶음 = 100;
            상태.처리Step = "05";
            // 완료
            var 완료목록 = 완료.Distinct().ToList();
            string SqlStr = "";
            for (int i = 0; i < 완료목록.Count; i += 묶음)
            {
                var part = 완료목록.Skip(i).Take(묶음).ToList();
                string 키목록 = $"'{string.Join("','", part)}'";
                SqlStr = $@"
                    UPDATE data 
                    SET PDF병합작업 = 'PDF병합완료'  
                    WHERE 처리구분 = '{상태.처리구분}' AND 
                          groupKey = '{상태.groupKey}' AND
                          고객Key IN ({키목록})
                    ";
                await DBUpdate(SqlStr, 상태.dataTable접속).ConfigureAwait(false);
            }

            // 오류
            var 오류목록 = 오류.Distinct().ToList();
            for (int i = 0; i < 오류목록.Count; i += 묶음)
            {
                var part = 오류목록.Skip(i).Take(묶음).ToList();
                string 키목록 = $"'{string.Join("','", part)}'";
                SqlStr = $@"
                    UPDATE data 
                    SET PDF병합작업 = 'PDF병합오류' 
                    WHERE 처리구분 = '{상태.처리구분}' AND
                          groupKey = '{상태.groupKey}' AND 
                          고객Key IN ({키목록})
                ";
                await DBUpdate(SqlStr, 상태.dataTable접속).ConfigureAwait(false);
            }
            // 주의요망(PDF병합작업)
            // F1 ~ F8은 모두 완료되서 Update 해야한다. 따라서 여기서는 업데이트를 하면 않된다.
            // 약관작업, PDF변환작업, PDF병합작업 병렬작업은 모든작업이 끝난 후에 일괄 업데이트 한다.
        }

        // ==========================================================================================================
        // 04 - 03 data 테이블 검색 : 처리구분(00~03,병렬처리), groupKey, PDF변환완료
        // ==========================================================================================================
        private async Task<SqlDataReader> LoadReaderAsync(현재상태 상태)
        {
            var AdoNet = new SqlConnection(상태.dataTable접속);
            await AdoNet.OpenAsync().ConfigureAwait(false);

            var cmd = new SqlCommand(@"
                SELECT *
                FROM data
                WHERE groupKey = @groupKey AND
                      처리구분 = @처리구분 AND
                      PDF변환작업 = 'PDF변환완료' AND
                      PDF병합작업 <> 'PDF병합완료'
                ", AdoNet);
            cmd.Parameters.AddWithValue("@groupKey", 상태.groupKey);
            cmd.Parameters.AddWithValue("@처리구분", 상태.처리구분);
            // CommandBehavior.CloseConnection: 리더 닫으면 커넥션도 닫힘
            return await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.CloseConnection).ConfigureAwait(false);
            // 쿼리가 PDF변환작업 <> 'PDF변환오류' 이유는 특별약관이 없은 경우(실손 H만 출력) PDF변환작업 값이 '대기'
            // 대기도 포함
        }

        // ==========================================================================================================
        // 04 - 04 선택약관PDF병합 : 세션별로 (H,B,T,I)를 하나로 병합하는 단계
        // ==========================================================================================================
        //private async Task<bool> 개별PDF세션결합(string 출력파일, List<string> tmp만들파일, string[] 분리, 현재상태 상태)
        //{
        //    int 재처리 = 0;
        //    bool 실행여부 = false;
        //    while (true)
        //    {
        //        if (재처리 >= 3)
        //        {
        //            break;
        //        }
        //        try
        //        {
        //            using (FileStream 약관fsRW = new FileStream(출력파일, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        //            using (PdfDocument 약관DOC = new PdfDocument(new PdfWriter(약관fsRW))) 
        //            {
        //                PdfMerger 약관Merger = new PdfMerger(약관DOC);
        //                약관Merger.SetCloseSourceDocuments(false);

        //                int 순서 = 0;
        //                string 이전세션 = "";
        //                int 짝홀페이지 = 0;
        //                string 현재파일명 = "";
        //                string 발송구분 = (분리[11] == "01" || 분리[11] == "1") ? "P" : (분리[11] == "02" || 분리[11] == "2") ? "M" : "E";  // P : 우편, M : 모바일, E : 이메일
        //                iText.Kernel.Geom.Rectangle pagesize = null;
        //                bool 처음만페이지 = false;
                       
        //                foreach (var 만들파일 in tmp만들파일)
        //                {
        //                    순서++;
        //                    string[] 파일명분리 = System.IO.Path.GetFileNameWithoutExtension(만들파일).Split('_');
        //                    string 세션 = 파일명분리[파일명분리.Length - 2];

        //                    현재파일명 = new FileInfo(만들파일).Name; // 에러확인용

        //                    // 세션별로 짝수페이지를 설정한다.
        //                    // 이전세션과 현재세션이 상이 하면서 이전세션의 페이지가 홀수이면 짝수로 세팅한다.
        //                    // 우편일때만 해당(이메일, 모바일제외)
        //                    // 현재세션이 목차이면 제외한다. ( 목차는 추가 하는 개념이 아니고 H에 삽입한다.)

        //                    if (순서 != 1 && 세션 != 이전세션 && 발송구분 == "P" && 세션 != "IX")
        //                    {
        //                        짝홀페이지 = 약관DOC.GetNumberOfPages();
        //                        // 목차가 있는 고객사
        //                        if (짝홀페이지 % 2 == 1)
        //                        {
        //                            약관DOC.AddNewPage((PageSize)pagesize);  //  *** 세션이 다르면 짝수페이지 설정 ***
        //                        }
        //                    }

        //                    using (FileStream 개별fsRW = new FileStream(만들파일, FileMode.Open, FileAccess.Read, FileShare.Read))
        //                    using (PdfDocument 개별DOC = new PdfDocument(new PdfReader(개별fsRW)))
        //                    {
        //                        if (처음만페이지 == false)
        //                        {
        //                            pagesize = 개별DOC.GetPage(1).GetPageSizeWithRotation();
        //                            처음만페이지 = true;
        //                        }

        //                        int 페이지 = 개별DOC.GetNumberOfPages();
        //                        //개별DOC.CopyPagesTo(1, 페이지, 약관DOC);   // CopyPage는 목차가 사라진다.

        //                        // 목차를 H에 삽입 하는 과정
        //                        // 목차는 Merge가  아니고 Insert을 해야 한다.

        //                        if (세션 == "IX")
        //                        {
        //                            int 삽입페이지 = Convert.ToInt32(파일명분리[파일명분리.Length - 1]);

        //                            // 일단 세션을 맞추기 위해 짝수 페이지를 맞쳐준다.
        //                            // 목차가 있는 고객사.
        //                            if (발송구분 == "P" && 삽입페이지 % 2 == 0)
        //                            {
        //                                약관DOC.AddNewPage(삽입페이지, (PageSize)pagesize);
        //                                삽입페이지 += 1;
        //                            }

        //                            // Insert 개념 ↓↓↓↓
        //                            // 증권Doc.CopyPagesTo(증권pagecnt, 증권pagecnt, 개별증권doc, 개별증권doc.GetNumberOfPages(), new PdfPageFormCopier());

        //                            개별DOC.CopyPagesTo(1, 페이지, 약관DOC, 삽입페이지, new PdfPageFormCopier());

        //                            // 보통약관이 시작하는 부분으로 홀수페이지 부터 시작 할 수 있도록 목차까지 삽입한 H부분을 짝수로 만들어 준다.
        //                            if (발송구분 == "P")
        //                            {
        //                                짝홀페이지 = 개별DOC.GetNumberOfPages();
        //                                int 현재까지페이지 = 삽입페이지 + 짝홀페이지 - 1;

        //                                if (짝홀페이지 % 2 == 1)
        //                                {
        //                                    // 빈페이지 삽입 (마지막 페이지)
        //                                    약관DOC.AddNewPage(현재까지페이지 + 1, (PageSize)pagesize);
        //                                }
        //                            }
        //                        }
        //                        else
        //                        {
        //                            약관Merger.Merge(개별DOC, 1, 페이지);
        //                            //약관Merger.Merge(개별DOC, iText.IO.Util.JavaUtil.ArraysAsList(1, 5, 7, 1));
        //                        }
        //                    }

        //                    이전세션 = 세션;
        //                }

        //                if (상태.고객사 == "CCK")
        //                {
        //                    PdfFont 나눔바른고딕 = PdfFontFactory.CreateFont(@"c:\Easy약관Plus\program\Font\NanumBarunGothic.ttf", PdfEncodings.IDENTITY_H);
        //                    Document docw = new Document(약관DOC);
        //                    int 전체페이지수 = 약관DOC.GetNumberOfPages();

        //                    for (int i = 1; i <= 전체페이지수; i++)
        //                    {
        //                        // 현재 페이지의 크기를 가져와 가로 중앙 위치(x) 설정
        //                        float x = 약관DOC.GetPage(i).GetPageSize().GetWidth() / 2;
        //                        float y = MMtoPoint(3.0); // 하단 여백 높이 

        //                        // "- 1 -" 형식의 쪽번호 텍스트 생성
        //                        string 쪽번호 = string.Format("- {0} -", i);

        //                        // 지정한 위치(x, y)에 쪽번호 삽입
        //                        docw.ShowTextAligned(
        //                            new Paragraph(쪽번호).SetFont(나눔바른고딕).SetFontSize(8).SetFontColor(iText.Kernel.Colors.ColorConstants.BLACK),
        //                            x, y, i, TextAlignment.CENTER, VerticalAlignment.BOTTOM, 0);
        //                    }
        //                }
        //                실행여부 = true;
        //                약관Merger.Close();

        //                //개별선택약관파일(H,B,IX,T)를 병합하고 '@완료' 폴더로 이동한다.
        //                string 완료폴더 = string.Format("{0}@완료\\", 상태.출력폴더);
        //                foreach (var 만들파일 in tmp만들파일)
        //                {
        //                    try
        //                    {
        //                        new FileInfo(만들파일).MoveTo(string.Format("{0}{1}", 완료폴더, new FileInfo(만들파일).Name));
        //                    }
        //                    catch (Exception)
        //                    {}
        //                }
        //                break;
        //            }
                    
        //        }
        //        catch (Exception ee)
        //        {
        //            로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[개별PDF세션결합 Er01] {1}==>{2}", DateTime.Now, ee.Message, ee.StackTrace);
        //            로그기록(로그내용, 1);
        //            재처리++;
        //            await Task.Delay(3000);
        //            continue;

        //        }
        //    }

        //    return 실행여부;
        //}
        private float MMtoPoint(double x)
        {
            float tmpx;
            tmpx = (float)(x * 2.83465);
            return tmpx;
        }

        //========================================================================================================================================
        // 공통
        //========================================================================================================================================

        // =============================================================================
        //  로그파일생성(공통)
        // =============================================================================
        private void 로그파일열기()
        {
            // ★ 파일 스트림 교체 중 충돌 방지
            lock (_logLock)
            {
                string 로그폴더생성 = 폴더생성(저장.로그경로, $"{DateTime.Now:yyyy.MM}");
                로그폴더생성 = 폴더생성(로그폴더생성, $"{DateTime.Now:dd}");

                DateTime now = DateTime.Now;

                if (Logwr != null) { Logwr.Flush(); Logwr.Close(); Logwr = null; }
                if (Logfs != null) { Logfs.Close(); Logfs = null; }

                string 로그파일 = string.Format("{0}({1:yyMMdd})H2PControl.log", 로그폴더생성, DateTime.Now);
                try
                {
                    Logfs = new FileStream(로그파일, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    Logwr = new StreamWriter(Logfs, Encoding.GetEncoding(949));
                    _currentLogDay = now.Day;
                }
                catch (Exception) { }
            }
        }

        // =============================================================================
        //  로그출력(공통)
        // =============================================================================

        private void 로그기록(string 내용, int 출력형태, int ID = 0)
        {
            if (실행주체 == "클라이언트")
                ID = 9;

            // ★ 1. 파일 쓰기 충돌 방지 (동기화)
            lock (_logLock)
            {
                try
                {
                    // Logwr이 null이 아닐 때만 쓰도록 방어
                    if (출력형태 == 1 && Logwr != null)
                    {
                        Logwr.WriteLine(내용);
                        Logwr.Flush();
                    }
                }
                catch (Exception) { }
            }

            // ★ 2. UI 조작은 백그라운드 스레드에서 직접 하면 위험하므로 BeginInvoke 처리
            this.BeginInvoke(new Action(() =>
            {
                try
                {
                    string 편집 = 내용;
                    if (내용.Length > 17)
                        편집 = 내용.Substring(17);

                    if (리스트[ID].Items.Count > 300)
                        리스트[ID].Items.RemoveAt(300);

                    리스트[ID].Items.Insert(0, 편집);
                    리스트[ID].SelectedIndex = 0;
                }
                catch { }
            }));
        }

        // =============================================================================
        //  리스트박스 재분배 그리기
        // =============================================================================
        private void H2PControl_Resize(object sender, EventArgs e)
        {
            // 폼이 최소화되었을 때는 크기 계산을 하지 않음
            if (this.WindowState == FormWindowState.Minimized) return;

            타이틀업데이트();

            int maxRoomId = 0;
            if (접속컴.Count > 0)
            {
                maxRoomId = 접속컴.Values.Max();
            }
            maxRoomId = Math.Min(5, maxRoomId);

            // 서브컴이 1대 이상 접속(또는 영역 확보) 되어 있을 때만 분할 계산 실행
            if (maxRoomId > 0)
            {
                // =========================================================================
                // ★ 화면 꽉 채우기 분할 로직 (빈틈 제로)
                // =========================================================================
                int startX = 508;          // 서브컴 리스트들이 시작될 고정 X좌표
                int padding = 2;           // 리스트박스 사이의 간격
                int rightMargin = 2;       // ★ 우측 끝 여백을 20에서 2로 대폭 줄였습니다. (테두리용 2px만 남김)

                // 분할할 전체 너비 (우측 2px 여백만 빼고 모두 사용)
                int availableWidth = this.ClientSize.Width - startX - rightMargin - (padding * (maxRoomId - 1));

                // 창이 너무 좁아지면 무시 (UI 깨짐 방지)
                if (availableWidth < 50) return;

                // 기본적으로 나눠가질 너비
                int listWidth = availableWidth / maxRoomId;
                int currentX = startX;

                // =========================================================================
                // ★ lsth2p(서버 리스트박스) 기준으로 남은 공간 완벽 분배 로직
                // =========================================================================
                if (리스트 != null && 리스트[0] != null)
                {
                    padding = 2;       // 리스트박스 사이의 간격
                    rightMargin = 2;   // 우측 끝 여백 (스크롤바/테두리용 최소 공간)

                    // 1. 시작점(startX) 계산: lsth2p의 오른쪽 끝부분 + 패딩
                    startX = 리스트[0].Left + 리스트[0].Width + padding;

                    // 2. 분할할 전체 너비 = 폼 전체 너비 - 시작점 - 우측여백 - (사이 간격들의 합)
                    availableWidth = this.ClientSize.Width - startX - rightMargin - (padding * (maxRoomId - 1));

                    // 창이 너무 좁아지면 UI 깨짐 방지를 위해 무시
                    if (availableWidth >= 50)
                    {
                        // 3. 기본 너비와 나누어 떨어지지 않는 '나머지 픽셀' 계산
                        listWidth = availableWidth / maxRoomId;
                        int remainder = availableWidth % maxRoomId;

                        currentX = startX;

                        for (int i = 1; i <= maxRoomId; i++)
                        {
                            if (i < 리스트.Length && 리스트[i] != null)
                            {
                                // (기존) 리스트박스 크기 및 위치 적용
                                int currentWidth = listWidth + (i <= remainder ? 1 : 0);
                                리스트[i].Left = currentX;
                                리스트[i].Width = currentWidth;

                                // =======================================================
                                // [추가] 라벨을 리스트박스 가로폭의 90%로 설정하고 
                                //        좌우 5%씩 공백을 두어 중앙에 배치합니다.
                                // =======================================================
                                if (i < 타일.Length && 타일[i] != null)
                                {
                                    // 1. 너비는 리스트박스 너비의 90%
                                    int labelWidth = (int)(currentWidth * 0.9);

                                    // 2. 남은 10% 공간의 절반을 여백으로 삼아 X좌표를 계산 (중앙 정렬)
                                    int labelLeft = currentX + (currentWidth - labelWidth) / 2;

                                    타일[i].Width = labelWidth;
                                    타일[i].Left = labelLeft;

                                    // =======================================================
                                    // ★ [추가] 라벨을 둥근 사각형으로 깎아냅니다.
                                    // =======================================================
                                    int 곡률 = 25; // 숫자가 커질수록 모서리가 더 둥글어집니다. (추천: 15~25)

                                    // 둥근 사각형 영역 핸들을 생성합니다.
                                    IntPtr hRgn = CreateRoundRectRgn(0, 0, 타일[i].Width, 타일[i].Height, 곡률, 곡률);

                                    // 라벨의 Region에 적용하여 외곽을 둥글게 자릅니다.
                                    타일[i].Region = System.Drawing.Region.FromHrgn(hRgn);

                                    // 사용한 GDI 객체 핸들을 삭제하여 메모리 누수를 방지합니다 (매우 중요!).
                                    DeleteObject(hRgn);
                                }

                                // (기존) 다음 리스트박스가 시작될 좌표 누적
                                currentX += currentWidth + padding;
                            }
                        }
                    }
                }
            }
        }

        // =============================================================================
        //  데이터 UPDATE/DELETE 상태(공통)
        // =============================================================================
        private async Task<int> DBUpdate(string SqlStr, string dataTable접속)
        {
            int 완료갯수 = -1;
            try
            {
                using (SqlConnection AdoNet = new SqlConnection(dataTable접속))
                {
                    await AdoNet.OpenAsync();
                    using (SqlCommand sqlcmd = new SqlCommand(SqlStr, AdoNet))
                    {
                        sqlcmd.CommandTimeout = 0;
                        완료갯수 = await sqlcmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception)
            {
            }

            return 완료갯수;
        }


        private async Task F1프로그램종료(int i)
        {
            // 1단계: PID 파일을 읽어서 '내 한컴'만 정확히 암살 (검색 X)
            string pidFilePath = string.Format(@"{0}PID\F{1}.PID", 저장.기본출력폴더, i);
            if (File.Exists(pidFilePath))
            {
                try
                {
                    // 1. 파일에서 PID 꺼내기
                    string pidStr = File.ReadAllText(pidFilePath);
                    if (int.TryParse(pidStr, out int targetPid))
                    {
                        try
                        {
                            Process p = Process.GetProcessById(targetPid);
                            if (!p.HasExited)
                            {
                                p.Kill();
                                p.WaitForExit(100);
                            }
                        }
                        catch (ArgumentException)
                        { // 이미 죽어서 없으면 OK
                        }
                    }
                }
                catch { }
                finally
                {
                    // 3. 증거 인멸 (PID 파일 삭제)
                    try
                    {
                        if (File.Exists(pidFilePath))
                        {
                            await Task.Delay(100); // 0.1초 대기 후 삭제 시도
                            File.Delete(pidFilePath);
                        }
                    }
                    catch { }
                }
            }

            // 2단계: F(i)번 앱 (f1.exe) 종료
            Process[] fAppProcesses = Process.GetProcessesByName("f1"); // 실행파일 이름 f1.exe
            string fAppFolder = string.Format(@"\F{0}\", i);            // 경로 구분용 (\F1\)

            foreach (Process p in fAppProcesses)
            {
                try
                {
                    if (p.HasExited) continue;
                    bool isTarget = false;

                    // [경로 확인] 가장 확실한 방법
                    try
                    {
                        if (p.MainModule.FileName.IndexOf(fAppFolder, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isTarget = true;
                        }
                    }
                    catch { /* 권한 문제 등 패스 */
                }

                // 타겟이면 종료
                if (isTarget)
                    {
                        p.Kill();
                        p.WaitForExit(100);
                    }
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        // =============================================================================
        //  폴더생성(공통)
        // =============================================================================
        private string 폴더생성(string 현재폴더, string 하위폴더)
        {
            string 하위폴더확인 = string.Format("{0}{1}\\", 현재폴더, 하위폴더);
            if (new System.IO.DirectoryInfo(하위폴더확인).Exists == false)
                new System.IO.DirectoryInfo(하위폴더확인).Create();
            return 하위폴더확인;
        }

        // =============================================================================
        //  복사, 이동, 삭제(공통)
        // =============================================================================
        private bool 복사_이동_삭제(string 명령, string 원본경로, string 대상경로)
        {
            
            bool 실행여부 = true;
            try
            {
                switch (명령)
                {
                    case "복사_삭제":
                        new FileInfo(원본경로).CopyTo(대상경로, true);
                        new FileInfo(원본경로).Delete();
                        break;
                    case "복사":
                        new FileInfo(원본경로).CopyTo(대상경로, true);
                        break;
                }
            }
            catch (Exception)
            {
                실행여부 = false;
            }
            return 실행여부;
        }

        // =======================================================================================================


        // 오류가 났을 때 호출할 함수
        public void SetError(bool isError, int index, string 오류종류)
        {
            if (index < 0 || index >= 리스트.Length) return;

            errorStates[index] = isError;
            var targetList = 리스트[index];

            if (isError)
            {
                if (오류종류 == "RECORDERR")
                {
                    targetList.Appearance.BackColor = Color.CornflowerBlue;
                    targetList.Appearance.Options.UseBackColor = true;
                }
                else
                {
                    // 오류 발생 시
                    targetList.Appearance.BackColor = Color.IndianRed;
                    targetList.Appearance.Options.UseBackColor = true;
                }
            }
            else
            {
                targetList.Appearance.Options.UseBackColor = false;
            }

            targetList.Invalidate(); // 여기서 Paint 이벤트를 호출합니다.
        }

        private void lsth2p0_5_Paint(object sender, PaintEventArgs e)
        {
            // 1. 지금 이벤트를 보낸 리스트박스가 누구인지 확인
            ListBoxControl currentList = sender as ListBoxControl;
            if (currentList == null) return;

            // 2. 이 리스트박스가 배열의 몇 번째 놈인지 찾기
            int index = Array.IndexOf(리스트, currentList);

            // 찾지 못했거나( -1 ), 해당 순번이 오류 상태가 아니면 그리지 않고 종료
            if (index < 0 || errorStates[index] == false) return;

            // ★ [핵심] 배경색(BackColor)을 보고 무슨 아이콘을 그릴지 결정합니다.
            System.Drawing.Image targetIcon = null;
            if (currentList.Appearance.BackColor == Color.CornflowerBlue)
            {
                targetIcon = errorIcon[1]; // RECORDERR 아이콘
            }
            else if (currentList.Appearance.BackColor == Color.IndianRed)
            {
                targetIcon = errorIcon[0]; // 일반 ERROR 아이콘
            }

            // 아이콘이 로드되지 않았으면 종료
            if (targetIcon == null) return;

            // 아이콘 크기 및 위치 계산 (정중앙)
            int iconSize = 250;
            int x = (currentList.Width - iconSize) / 2;
            int y = (currentList.Height - iconSize) / 2;

            // 투명도 설정
            ColorMatrix matrix = new ColorMatrix();
            matrix.Matrix33 = 0.6f; // 투명도 60%
            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

            // 그리기
            e.Graphics.DrawImage(targetIcon, new System.Drawing.Rectangle(x, y, iconSize, iconSize),
                0, 0, targetIcon.Width, targetIcon.Height, GraphicsUnit.Pixel, attributes);
        }


        private void 화면초기화_LED()
        {
            // 2. 디자이너에서 만든 sF1 ~ sF8을 배열에 차례대로 담아줍니다.
            f_indicators = new StateIndicatorComponent[] {sF1, sF2, sF3, sF4, sF5, sF6, sF7, sF8 };

            // 3. 프로그램 시작 시 모두 '대기' 상태(불 꺼짐)로 초기화합니다.
            for (int i = 0; i < 8; i++)
            {
                _currentStates[i] = "WAITING";
                f_indicators[i].StateIndex = 0;
            }
        }

        /// <summary>
        /// 특정 프로세스의 상태를 업데이트합니다.
        /// </summary>
        /// <param name="index">프로세스 인덱스 (0~7은 각각 F1~F8을 의미)</param>
        /// <param name="status">상태 ("RUNNING", "ERROR", "WAITING")</param>
        private void UpdateFProcessState(int index, string status)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateFProcessState(index, status)));
                return;
            }

            _currentStates[index] = status;
            var indicator = f_indicators[index];

            switch (status)
            {
                case "RUNNING":  // 실행 중
                    indicator.StateIndex = 3; // ★ 초록불 (3)
                    break;
                case "ERROR":    // 오류
                    indicator.StateIndex = 1; // ★ 빨간불 (1)
                    break;
                default:         // 대기
                    indicator.StateIndex = 0; // ★ 꺼짐 (0)
                    break;
            }
        }

        private bool isLedOn = true;

        private void blinkTimer_Tick(object sender, EventArgs e)
        {
            // 1. 0.5초마다 플래그를 뒤집습니다. (On -> Off -> On)
            isLedOn = !isLedOn;

            // 2. 8개의 LED 상태를 확인하고 화면을 갱신합니다.
            for (int i = 0; i < 8; i++)
            {
                if (_currentStates[i] == "RUNNING")
                {
                    // "실행 중"인 프로세스만 초록불(1)과 꺼짐(0)을 왔다 갔다 합니다.
                    f_indicators[i].StateIndex = isLedOn ? 1 : 0;
                }
                else if (_currentStates[i] == "ERROR")
                {
                    // "오류"인 프로세스는 빨간불(3)로 계속 고정시켜 경고를 확실히 줍니다.
                    f_indicators[i].StateIndex = 3;
                }
                else
                {
                    // "대기"인 프로세스는 회색(0)으로 고정합니다.
                    f_indicators[i].StateIndex = 0;
                }
            }
        }

        private void 컨트롤러초기화()
        {
            리스트 = new ListBoxControl[] {
                lsth2p,
                lsth2p_1,
                lsth2p_2,
                lsth2p_3,
                lsth2p_4,
                lsth2p_5,
                null,
                null,
                null,
                lbxMsg
            };

            타일 = new DevExpress.XtraEditors.LabelControl[]
            {
                null,      // 0번 인덱스는 비워둠 (1~5번 서브컴과 인덱스를 맞추기 위함)
                lbl_01,
                lbl_02,
                lbl_03,
                lbl_04,
                lbl_05
            };

            // 타일 내부 텍스트가 항상 정중앙에 오도록 강제 정렬
            for (int i = 1; i < 타일.Length; i++)
            {
                if (타일[i] != null)
                {
                    // 자동 크기 조절을 꺼야 코드로 90% 비율 크기를 자유롭게 제어할 수 있습니다.
                    타일[i].AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;

                    // HTML 텍스트 파싱 허용
                    타일[i].AllowHtmlString = true;
                    타일[i].Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;

                    // 텍스트 정중앙 정렬
                    타일[i].Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
                    타일[i].Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Center;

                    // 폰트 및 색상 지정 (TileItem의 AppearanceItem.Normal -> Appearance로 변경됨)
                    타일[i].Appearance.Font = new System.Drawing.Font("카이겐고딕 KR Bold", 15.75f, System.Drawing.FontStyle.Bold);
                    타일[i].Appearance.BackColor = Color.DimGray;
                    타일[i].Appearance.ForeColor = Color.White;

                    // 초기 화면 텍스트 할당
                    string 빈상태바 = "<color=#D3D3D3>□□□□□□□□□□</color> 0%";
                    타일[i].Text = $"<size=30>📄대기 : 00</size><br><size=10> </size><br><size=40><color=Silver>🕒 00:00</color></size><br><size=10> </size><br><size=18>{빈상태바}</size>";
                }
            }

            서브컴작업시간타이머.Interval = 1000; // 1초마다 실행
            서브컴작업시간타이머.Tick += 서브컴작업시간타이머_Tick;
            서브컴작업시간타이머.Start();


            //서버(0: main), 클라이언트(9)  
            errorStates = new bool[리스트.Length];

            errorIcon[0] = Properties.Resources.오류1;
            errorIcon[1] = Properties.Resources.오류2;

            foreach (var lb in 리스트)
            {
                if (lb != null)
                {
                    lb.HighlightedItemStyle = DevExpress.XtraEditors.HighlightStyle.Standard;
                    lb.Paint += lsth2p0_5_Paint;
                    lb.DrawItem += 공통_ListBox_DrawItem;
                }
                    
            }
        }

        // =========================================================================
        // ★ [추가] 1초마다 실행되며 배열(시간)의 라벨들을 업데이트하는 프로시저
        // =========================================================================
        private void 서브컴작업시간타이머_Tick(object sender, EventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                foreach (var kvp in 서브컴작업시작시간.ToList())
                {
                    string 컴구분 = kvp.Key;
                    DateTime 시작시간 = kvp.Value;
                    TimeSpan 경과 = DateTime.Now - 시작시간;

                    string 번호문자열 = 컴구분.Replace("sub", "").Replace("Ksub", "");

                    if (int.TryParse(번호문자열, out int 인덱스))
                    {
                        if (인덱스 >= 1 && 인덱스 < 타일.Length && 타일[인덱스] != null)
                        {
                            int 건수 = 서브컴할당건수.ContainsKey(컴구분) ? 서브컴할당건수[컴구분] : 0;
                            int 타임아웃분 = 서브컴할당타임아웃.ContainsKey(컴구분) ? 서브컴할당타임아웃[컴구분] : 10;

                            string 시간텍스트 = $"{(int)경과.TotalMinutes:D2}:{경과.Seconds:D2}";
                            double 경고시점_분 = 타임아웃분 * (2.0 / 3.0);
                            double 타임아웃_초 = 타임아웃분 * 60.0;

                            // 진행률 상태바 계산 (최대 100%, 10칸)
                            double 진행률 = (경과.TotalSeconds / 타임아웃_초) * 100.0;
                            if (진행률 > 100) 진행률 = 100;

                            Color 배경색 = (경과.TotalMinutes >= 경고시점_분) ? Color.IndianRed : Color.SteelBlue;
                            string 상태바채움색상 = (경과.TotalMinutes >= 경고시점_분) ? "#FFFF00" : "#00FF00";

                            // =================================================================
                            // ★ 1줄 고정을 위해 10칸(10%당 1칸)으로 축소 및 안전 고정
                            // =================================================================
                            int 채운칸 = (int)(진행률 / 10.0);
                            채운칸 = Math.Max(0, Math.Min(10, 채운칸));

                            string 바_채움 = new string('■', 채운칸);
                            string 바_빈칸 = new string('□', 10 - 채운칸);

                            타일[인덱스].Appearance.BackColor = 배경색;
                            타일[인덱스].Appearance.ForeColor = Color.White;

                            string 상태바 = $"<color={상태바채움색상}>{바_채움}</color><color=#D3D3D3>{바_빈칸}</color> {진행률:F0}%";
                            타일[인덱스].Text = $"<size=30>📄처리 : {건수:00}</size><br><size=10> </size><br><size=40><color=Orange>🕒 {시간텍스트}</color></size><br><size=10> </size><br><size=18>{상태바}</size>";
                        }
                    }
                }
            }));
        }



        /// <summary>
        /// 4단계 진행 바(StepProgressBar) 상태 업데이트
        /// 0: 접속, 1: 대기, 2: 진행, 3: 완료
        /// </summary>
        private void 시스템상태업데이트(int stepIndex)
        {
            // 크로스 스레드 에러 방지
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => 시스템상태업데이트(stepIndex)));
                return;
            }

            if (stepProg != null)
            {
                stepProg.SelectedItemIndex = stepIndex; // 지정된 단계로 애니메이션 이동
            }
        }

        private void 작업상태아이콘업데이트()
        {
            // 크로스 스레드(Cross-Thread) 오류 방지
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(작업상태아이콘업데이트));
                return;
            }
        }

        // 모든 리스트박스가 공유할 공통 DrawItem 이벤트
        private void 공통_ListBox_DrawItem(object sender, DevExpress.XtraEditors.ListBoxDrawItemEventArgs e)
        {
            if (e == null || e.Index < 0) return;

            DevExpress.XtraEditors.ListBoxControl currentList = sender as DevExpress.XtraEditors.ListBoxControl;

            // ★ 1. [추가] 지금 그림을 그리는 리스트박스가 배열의 몇 번째 놈인지 찾습니다.
            int listIndex = Array.IndexOf(리스트, currentList);

            string text = e.Item != null ? e.Item.ToString() : string.Empty;

            // 기본 스타일 세팅
            Color bgColor = System.Drawing.Color.FromArgb(190, 190, 190);
            Color fgColor = System.Drawing.Color.Black;
            Font textFont = new Font(e.Appearance.Font, System.Drawing.FontStyle.Regular);

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool isKeyword = false;

            // 키워드별 역상 강조 (기존과 동일)
            if (실행주체 == "클라이언트" && text.Contains("[**작업완료**]")) { /* 생략(기존유지) */ bgColor = System.Drawing.Color.Blue; fgColor = System.Drawing.Color.White; isKeyword = true; }
            else if (text.Contains("[관제탑완료]")) { /* 생략(기존유지) */ bgColor = System.Drawing.Color.Purple; fgColor = System.Drawing.Color.White; isKeyword = true; }
            else if (currentList != null && currentList.Name == "lsth2p" && text.Contains("[파일전송]")) { /* 생략(기존유지) */ bgColor = System.Drawing.Color.DarkGreen; fgColor = System.Drawing.Color.White; isKeyword = true; }
            else if (text.Contains("[ERROR]") || text.Contains("[FROZEN]") || text.Contains("[오류]")) { /* 생략(기존유지) */ bgColor = System.Drawing.Color.IndianRed; fgColor = System.Drawing.Color.White; isKeyword = true; }

            // 선택(Selected) 상태 처리 (기존과 동일)
            if (isSelected)
            {
                if (!isKeyword) { bgColor = SystemColors.Highlight; fgColor = SystemColors.HighlightText; }
                else { textFont = new Font(e.Appearance.Font, System.Drawing.FontStyle.Bold); }
            }

            // ====================================================================
            // ★ 2. [핵심 추가] 만약 지금 이 리스트박스가 '깜빡임' 상태라면?
            // 다른 모든 색상 규칙을 무시하고 강제로 하늘색으로 덮어버립니다!
            // ====================================================================
            if (listIndex >= 0 && 리스트깜빡임상태[listIndex] == true)
            {
                bgColor = Color.LightSkyBlue; // 깜빡임 배경색
                fgColor = Color.Black;        // 글자색은 보기 편하게 검정
            }

            // 최종 설정된 색상과 폰트를 바탕으로 그리기
            e.Appearance.BackColor = bgColor;
            e.Appearance.ForeColor = fgColor;
            e.Appearance.Font = textFont;

            e.Appearance.FillRectangle(e.Cache, e.Bounds);

            System.Drawing.Rectangle textBounds = new System.Drawing.Rectangle(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height);
            e.Appearance.DrawString(e.Cache, text, textBounds);

            e.Handled = true;
        }

        // =============================================================================
        //  Server/Client 초기화(접속)
        // =============================================================================
        public async Task<string> TCPServer_Client초기화(string tmp실행주체, string 시작종료, 환경설정 설정)
        {
            string 실행여부 = "성공";
            bool 작동스위치 = true;
            if (tmp실행주체 == "서버")
            {
                if (시작종료 == "시작")
                {
                    _roomManager = new ClientRoomManager();
                    _server = new ChatServer(IPAddress.Parse(설정.접속IP), 설정.접속PORT);  //192.168.1.20:5150, 127.0.0.1:8080(로컬)
                    _server.Connected += S_Connected;
                    _server.Disconnected += S_Disconnected;
                    _server.Received += S_Received;

                    int 접속시도 = 0;
                    int 한번출력 = 1;
                    while (true)
                    {

                        _ = _server.StartAsync();
                        if (_server.IsRunning)
                        {
                            this.Invoke(new Action(() =>
                            {
                                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[서버구동] === SERVER Start ===";
                                로그기록(로그내용, 1);
                            }));
                            시스템상태업데이트(0); // 0단계: 접속 완료
                            실행여부 = "성공";

                            if (chk싱글처리.Checked && SystemInformation.ComputerName.ToLower() == "Kmain")
                            {
                                this.Invoke(new Action(() =>
                                {
                                    Start통약관Monitoring();
                                }));
                            }

                            break;
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[서버구동시도] SERVER 구동시도 {접속시도++:00000}";

                            실행여부 = "실패";
                            로그기록(로그내용, 한번출력);
                            if (한번출력 == 1)
                                한번출력 = 0;
                        }
                    }

                } else if (시작종료 == "종료")
                {
                    _server.Stop();
                    작동스위치 = false;
                }
                
            }
            else if (tmp실행주체 == "클라이언트")
            {
                if (시작종료 == "시작")
                {
                    _client = new ChatClient(IPAddress.Parse(설정.접속IP), 설정.접속PORT);
                    _client.Connected += C_Connected;
                    _client.Disconnected += C_Disconnected;
                    _client.Received += C_Received;

                    int 접속시도 = 0;
                    int 한번출력 = 1;
                    while (true)
                    {
                        await _client.ConnectAsync(new ConnectionDetails
                        {
                            RoomId = C_RoomId,
                            UserName = C_UserName,
                        });

                        if (_client.IsRunning)
                        {                           
                            this.Invoke(new Action(() =>
                            {
                                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[서버접속] == SERVER Connection == ";
                                로그기록(로그내용, 1);
                            }));

                            실행여부 = "성공";
                            break;
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[서버접속시도] 서버접속시도 {접속시도++:00000}";
                            실행여부 = "실패";
                            로그기록(로그내용, 한번출력);
                            if (한번출력 == 1)
                                한번출력 = 0;
                        }
                    }
                }
                else if (시작종료 == "종료")
                {
                    _client.Close();
                    작동스위치 = false;
                }
                
            }
            RunningStateChanged(작동스위치);
            return 실행여부;
        }


        // =============================================================================
        //  TCP/IP (Sverver)
        // =============================================================================


        // =============================================================================
        //  sub컴완료 확인 후 맞춤약관(main)에 *.전손완료
        //  1) server는 client에서 작업이 완료 될때까지 대기한다.(client는 Pdf병합완료 후 server로 Pdf를 전송하고 마지막을 sub1.END 전송한다.)
        //  2) 서버 : 대기 , 클라이언트 : 작업완료 후 PDF파일,*.END파일 전송
        //  3) 서버 : 모든 sub컴이 완료되면 main컴에 다음단계로 진행하기 위해 *.전송완료를 전달한다.
        //  4) 메인컴 : *.전송완료 수신 후 다음단계로 작업을 진행
        //
        //  클라이언트 : PDF병합이 완료되면 서버로 PDF파일, END파일을 전송한다.
        // =============================================================================
        private async Task<string> 서버수신대기_클라이언트파일송신(현재상태 상태, string 실행주체, CancellationToken token = default)
        {
            string 실행여부 = "성공";


            if (실행주체 == "서버")
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        string tmpDirPath = @"d:\Easy약관Plus\Output\TmpFile\";
                        bool Tmp존재유무 = false;
                        FileInfo[] endFiles_가져옴 = new FileInfo[0];
                        FileInfo[] endFiles_Tmp = new FileInfo[0];

                        // 💡 1. Tmp폴더가 존재할 때만 파일 목록을 가져오도록 수정 (DirectoryNotFound 예외 완벽 방지)
                        if (Directory.Exists(tmpDirPath))
                        {
                            DirectoryInfo tmpDirInfo = new DirectoryInfo(tmpDirPath);
                            endFiles_가져옴 = tmpDirInfo.GetFiles("*.가져옴"); 
                            endFiles_Tmp = tmpDirInfo.GetFiles("*.END");
                            Tmp존재유무 = true;

                            this.Invoke(new Action(() =>
                            {
                                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[main컴] 임시폴더에서 복사 : {tmpDirPath}";
                                로그기록(로그내용, 0);
                            }));
                        }

                        FileInfo[] endFiles = new DirectoryInfo($"{상태.출력폴더}StateFile\\").GetFiles("*.END");

                        int ENDFcnt = (Tmp존재유무 && endFiles_가져옴.Length == 0) ? endFiles.Length + endFiles_Tmp.Length : endFiles.Length;

                        if (ENDFcnt == 상태.병렬컴 - 1)
                        {
                            if (Tmp존재유무 && endFiles_Tmp.Length >= 1 && endFiles_가져옴.Length == 0)
                            {
                                bool 이동성공 = true;
                                try
                                {
                                    // 💡 2. *.가져옴 마커 파일 먼저 생성 (다른 스레드나 프로세스가 접근하지 못하게 락(Lock) 역할)
                                    string 마커파일 = System.IO.Path.Combine(tmpDirPath, $"{상태.groupKey}.가져옴");
                                    File.WriteAllText(마커파일, "이동중", Encoding.GetEncoding(949));

                                    // 💡 3. PDF 파일 이동
                                    string targetPdfDir = $"{상태.출력폴더}Y\\";
                                    if (!Directory.Exists(targetPdfDir)) Directory.CreateDirectory(targetPdfDir);

                                    foreach (FileInfo f in new DirectoryInfo(tmpDirPath).GetFiles("*.pdf"))
                                    {
                                        string targetPath = System.IO.Path.Combine(targetPdfDir, f.Name);
                                        if (File.Exists(targetPath)) File.Delete(targetPath); // 혹시 모를 중복 덮어쓰기 방지
                                        f.MoveTo(targetPath);
                                    }

                                    // 💡 4. END 파일 이동
                                    string targetEndDir = $"{상태.출력폴더}StateFile\\";
                                    if (!Directory.Exists(targetEndDir)) Directory.CreateDirectory(targetEndDir);

                                    foreach (FileInfo f in new DirectoryInfo(tmpDirPath).GetFiles("*.END"))
                                    {
                                        string targetPath = System.IO.Path.Combine(targetEndDir, f.Name);
                                        if (File.Exists(targetPath)) File.Delete(targetPath);
                                        f.MoveTo(targetPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    이동성공 = false;
                                    // 이동 중 에러가 발생하면 로그를 남기고 TmpFile 폴더를 보존하여 데이터 유실을 막습니다.
                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[이동오류] Tmp 파일 이동 실패: {ex.Message}", 1);
                                }

                                // 💡 5. 이동이 완벽하게 성공했을 때만 TmpFile 폴더 전체 삭제
                                if (이동성공)
                                {
                                    try
                                    {
                                        // true 옵션을 주면 폴더 안의 내용물(*.가져옴 포함)까지 한 번에 싹 삭제됩니다.
                                        Directory.Delete(tmpDirPath, true);
                                    }
                                    catch (Exception)
                                    {
                                        // 이미 지워졌거나 사용 중이어서 지우지 못해도 프로그램 흐름에는 영향을 주지 않도록 무시
                                    }

                                    // 💡 6. [핵심] Tmp 파일들을 StateFile 폴더로 모두 옮겼으므로, 
                                    // 최신 상태를 반영하기 위해 endFiles 목록을 하드디스크에서 다시 한번 정확하게 읽어옵니다.
                                    endFiles = new DirectoryInfo($"{상태.출력폴더}StateFile\\").GetFiles("*.END");
                                }
                                else
                                {
                                    // 이동 실패 시 다음 사이클을 기약하며 일단 루프를 넘깁니다.
                                    await Task.Delay(TimeSpan.FromSeconds(3));
                                    continue;
                                }
                            }

                            //{상태.groupKey}.전송완료 파일을 기준으로 모든파일이 전송완료를 의미한다.(메인프로그램 참조)
                            string EndofFile = $"{상태.출력폴더}StateFile\\{상태.groupKey}.전송완료";
                            string 출력내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[전송완료] : 서브컴 {1:00000} ", DateTime.Now, ENDFcnt);
                            File.WriteAllText(EndofFile, 출력내용, Encoding.GetEncoding(949));

                            lbJobs.Items.Clear();
                            작업상태아이콘업데이트();

                            foreach (FileInfo file in endFiles)
                            {
                                // 파일명에서 확장자를 제외한 이름(예: "sub1")을 추출
                                string 대상컴 = System.IO.Path.GetFileNameWithoutExtension(file.Name);

                                await _server.서버To클라이언트송신(대상컴, new ChatHub
                                {
                                    SendType = SendType.ServerToOne,
                                    State = ChatState.Message,
                                    플레그 = "S To C : 메세지전송",
                                    Message = "*맞춤약관작업완료*",
                                    UserName = 상태.상태컴퓨터,
                                    RoomId = 0
                                });

                                // 연속 전송 시 엉키지 않도록 아주 짧은 간격을 줍니다.
                                await Task.Delay(100);
                            }

                            // Message = "*맞춤약관작업완료*" 이값을 클라이언트에서 받으면 작업을 완료(모든 데이터삭제) 다음 레코드 처리 대기 상태로 간다.

                            int 대기중cnt = 0;
                            while (true)
                            {
                                // [타임아웃 감시] 내부 루프에서도 20분 초과 감시
                                token.ThrowIfCancellationRequested();

                                string 작업완료파일 = $"{저장.기본출력폴더}감시\\{상태.groupKey}.main작업완료";
                                if (new FileInfo(작업완료파일).Exists)
                                {
                                    new FileInfo(작업완료파일).Delete();
                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[main컴] mainCOM ===작업완료===", 1);
                                    break;
                                }
                                else
                                {
                                    대기중cnt++;
                                    this.Invoke(new Action(() => {
                                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[main컴] mainCOM 작업진행중...{대기중cnt}", 0);
                                    }));

                                    // Delay에도 token을 주어 즉각적인 강제 취소가 가능하게 합니다.
                                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                                    continue;
                                }
                            }
                            break;
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(3), token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 타임아웃 예외는 상위(Main)로 던져서 강제초기화를 유도함
                    }
                    catch (Exception)
                    {
                        // 일반 예외 무시 로직
                    }
                }
            }
            else if(실행주체 == "클라이언트")
            {

                while (true)
                {

                    try
                    {
                        if (_clientHandler == null || _client == null || !_client.IsRunning)
                        {
                            // 재접속은 어차피 백그라운드(C_Disconnected)에서 하고 있으니, 
                            // 여기서는 3초씩 쉬면서 연결이 복구되었는지만 확인합니다.
                            await Task.Delay(TimeSpan.FromSeconds(3), token);
                            continue;
                        }
                        // 세션별로 분리되어 있던 파일을 PDF병합을 통해 출력\\Y에 저장한다.
                        // \\Y폴더에 있는 파일을 서버의 \\Y폴더로 전송한다.
                        string 약관전송폴더 = $"{저장.출력폴더}Y\\";
                        var files = Directory.GetFiles(약관전송폴더, "*.pdf");

                        로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[준비]  파일전송 파일  : {files.Length}";
                        로그기록(로그내용, 1);
                                               
                        string 플레그 = $"C To S : 개별PDF전송";
                        await _clientHandler.파일전송(files, C_RoomId, C_UserName, 플레그);

                        로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[완료] 파일전송 완료 : {files.Length}";
                        로그기록(로그내용, 1);

                        // =========================================================================
                        // 오류가 났던 고객이 있다면 서버에 '재할당 요청' 패킷 전송
                        // =========================================================================
                        if (상태.오류고객목록 != null && 상태.오류고객목록.Count > 0)
                        {
                            string 오류pk문자열 = string.Join(",", 상태.오류고객목록);
                            _clientHandler.Send(new ChatHub
                            {
                                RoomId = C_RoomId,
                                UserName = C_UserName,
                                State = ChatState.진행상황,
                                상황 = "재할당요청",
                                Message = 오류pk문자열
                            });
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[재할당요청] {상태.오류고객목록.Count}명 메인 서버로 반환 완료", 1);
                        }

                        break; // 전송 성공 시 탈출
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 상위로 전달
                    }
                    catch (Exception)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), token);
                    }
                }

            } 
            else
            {
                실행여부 = "실패";
                
            }
            return 실행여부;
        }

        private void 강제초기화_및_재시작처리(string 실행주체)
        {
            try
            {
                string 감시폴더 = $"{저장.기본출력폴더}감시\\";
                if (!Directory.Exists(감시폴더)) Directory.CreateDirectory(감시폴더);

                string 신호파일 = System.IO.Path.Combine(감시폴더, "강제재시작.종료");

                if (실행주체 == "서버")
                {
                    // 서버 본인이 감지: 바로 자신의 로컬에 파일 생성
                    if (!File.Exists(신호파일))
                    {
                        File.WriteAllText(신호파일, "TIMEOUT_SERVER", Encoding.GetEncoding(949));
                    }
                }
                else if (실행주체 == "클라이언트")
                {
                    // 클라이언트 감지: 파일이 없을 때만 서버에 요청 전송 (무한 반복 방지 핵심)
                    if (!File.Exists(신호파일))
                    {
                        _clientHandler?.Send(new ChatHub
                        {
                            RoomId = C_RoomId,
                            UserName = C_UserName,
                            Message = "[요청]강제초기화",
                            상황 = "에러",
                            State = ChatState.Message
                        });
                        로그기록($"{DateTime.Now : yy.MM.dd HH:mm:ss}[시스템] 타임아웃 감지. 서버에 초기화 요청 전송 완료.", 1);
                    }
                }
            }
            catch (Exception ex)
            {
                로그기록($"{DateTime.Now: yy.MM.dd HH:mm:ss}[오류] 재시작 처리 중 에러: {ex.Message}", 1);
            }
        }

        private void 폴더내부파일모두삭제()
        {
            string[] 삭제폴더 = new string[2] { "감시", "List" };
            string 리스트폴더 = $"{저장.기본출력폴더}list\\";

            foreach (string tmp in 삭제폴더)
            {
                string 폴더 = $"{저장.기본출력폴더}{tmp}\\";
                if (!Directory.Exists(폴더)) 
                    continue;

                try
                {
                    // 폴더 안의 모든 파일 삭제
                    foreach (string file in Directory.GetFiles(폴더))
                        File.Delete(file);

                }
                catch (Exception ex)
                {
                    // 파일 잠금 등으로 삭제 실패 시 로그만 남김
                    로그기록($"폴더 비우기 실패({폴더}): {ex.Message}", 1);
                }

            }

        }


        /// <summary>
        /// 단독 처리 모드일 때, 메인 프로그램(frm담보Merge)이 작업을 완전히 끝마칠 때까지 대기합니다.
        /// </summary>
        private async Task 단독Main작업완료대기(현재상태 상태, CancellationToken token = default)
        {
            int 대기중cnt = 0;
            string 작업완료파일 = $"{저장.기본출력폴더}감시\\{상태.groupKey}.main작업완료";

            while (true)
            {
                // 상위의 CancellationToken 감시 (타임아웃 연동)
                token.ThrowIfCancellationRequested();

                if (File.Exists(작업완료파일))
                {
                    int 삭제재시도 = 0;

                    // ★ [안전장치] 삭제를 최대 3번까지만 시도하여 무한루프를 방지합니다.
                    while (삭제재시도 < 3)
                    {
                        try
                        {
                            await Task.Delay(100); // 락 해제 대기
                            File.Delete(작업완료파일);
                            break; // 성공적으로 지웠으면 삭제 루프 탈출
                        }
                        catch (IOException)
                        {
                            삭제재시도++;
                            if (삭제재시도 >= 3)
                            {
                                // 3번이나 실패했다면 영구적인 락으로 간주하고 경고 로그만 남긴 뒤 무시합니다.
                                // (어차피 다음 작업은 groupKey가 달라서 이 파일과 충돌하지 않습니다)
                                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[경고] 파일 잠금 지속으로 삭제 건너뜀 (작업은 정상 진행)", 1);
                                break;
                            }
                            await Task.Delay(500); // 0.5초 쉬었다가 다시 지워봄
                        }
                    }

                    로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[main컴] 단독 mainCOM ===작업완료===";
                    로그기록(로그내용, 1);

                    if (상태.오류고객목록 != null && 상태.오류고객목록.Count > 0)
                    {
                        string 오류pk문자열 = string.Join(",", 상태.오류고객목록);

                        _clientHandler?.Send(new ChatHub
                        {
                            RoomId = C_RoomId,
                            UserName = C_UserName,
                            State = ChatState.진행상황,
                            상황 = "재할당요청",
                            Message = 오류pk문자열
                        });

                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[재할당요청] {상태.오류고객목록.Count}명 메인 서버로 반환 완료 (싱글처리)", 1);
                    }



                    break; // 전체 대기 무한루프를 완벽하게 탈출하여 다음 단계로 넘어감!
                }
                else
                {
                    대기중cnt++;
                    this.Invoke(new Action(() =>
                    {
                        로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[main컴] 단독 mainCOM 작업진행 대기중...{대기중cnt}";
                        로그기록(로그내용, 대기중cnt == 1 ? 1 : 0);
                    }));

                    await Task.Delay(TimeSpan.FromSeconds(2), token); // token을 넣어 즉시 취소 지원
                }
            }
        }


        private ChatHub CreateNewStateChatHub(ChatHub hub, ChatState state)
        {
            return new ChatHub
            {
                RoomId = hub.RoomId,
                UserName = hub.UserName,
                State = state,
            };
        }
        private void AddClientMessageList(ChatHub hub)
        {
            string message;
            switch (hub.State)
            {
                case ChatState.Connect:
                    message = $"[접  속] {hub.UserName}";
                    break;
                case ChatState.Disconnect:
                    message = $"[종  료] {hub.UserName}";
                    break;
                case ChatState.FileStart:
                case ChatState.FileChunk:
                case ChatState.FileEnd:
                case ChatState.Ack:
                    message = hub.Message;
                    break;
                case ChatState.진행상황:
                    message = hub.Message;
                    if (hub.상황 == "오류")
                    { 
                        string 오류상태 = "";
                        if (message.Contains("RECORDERR"))
                            오류상태 = "RECORDERR";
                        else if (message.Contains("ERROR"))
                            오류상태 = "ERROR";
                        else if (message.Contains("FROZEN"))
                            오류상태 = "FROZEN";

                        SetError(true, hub.RoomId, 오류상태) ;
                    }
                    else if (hub.상황 == "재할당요청" && 실행주체 == "서버")
                    {
                        string[] errorPks = hub.Message.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        string clientName = hub.UserName.ToLower(); // 예: "sub3", "main" 등 발신 클라이언트 식별자

                        // DB 추출 및 파일 생성을 위해 백그라운드 스레드로 넘김 (클라이언트 이름도 전달)
                        Task reallocTask = Task.Run(async () => await 재할당처리_비동기(errorPks, clientName));
                        _재할당작업목록[clientName] = reallocTask;

                        message = $"[수신] {hub.UserName}로부터 {errorPks.Length}건 재할당 요청 수신 (백업 검색 타겟: *.{clientName})";


                    }

                    break;
                default:
                    //ChatState.FileStart으로 제어를 해야 하나 공용으로 사용해서 중간에 값이 변경되면 처음한번만 되는것이 아니고 2번 반복된다.
                    if (hub.Message.StartsWith("[수신시작]"))
                        message = hub.Message;
                    else
                        message = $"{hub.UserName} : {hub.Message}";
                    
                    break;
            }
            // case ChatState.Ack : 서버에서 클라이언트로 파일수신완료 응답메세지

            int 표시 = 1;
            if (message.Contains("[RUNNING]") || message.Contains("[WAITING]"))
            {
                표시 = 0;
            }
            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}{message}", 표시, hub.RoomId);


            if (실행주체 == "서버" && chk싱글처리.Checked)
            {
                // 서브컴이 보내는 완료 메시지 감지 (*맞춤약관작업완료* 또는 [**작업완료**])
                if (hub.Message.Contains("*맞춤약관작업완료*") || hub.Message.Contains("[**작업완료**]"))
                {
                    // 전체 완료 처리 로직을 백그라운드로 넘겨 서버의 통신 수신을 방해하지 않게 합니다.
                    Task.Run(async () =>
                    {
                        try
                        {
                            // =========================================================================
                            // ★ [근본 해결책: 스레드 동기화]
                            // 만약 이 클라이언트가 방금 재할당 작업을 요청했다면, 
                            // 그 작업이 파일을 다 읽고 안전하게 끝날 때까지(await) 여기서 얌전히 기다립니다.
                            // =========================================================================
                            if (_재할당작업목록.TryRemove(hub.UserName, out Task pendingTask))
                            {
                                await pendingTask; // 충돌 원천 차단!
                            }

                            // =========================================================================
                            // 재할당 작업이 끝났으므로(또는 없었으므로) 이제 파일은 100% 안전합니다.
                            // 맘 편히 삭제를 진행합니다. (에러가 날 확률 0%)
                            // =========================================================================
                            string tmpInputPath = $@"{저장.기본출력폴더}감시\TmpInput\";
                            if (Directory.Exists(tmpInputPath))
                            {
                                FileInfo[] files = new DirectoryInfo(tmpInputPath).GetFiles($"*.{hub.UserName}");
                                foreach (FileInfo f in files)
                                {
                                    if (f.Exists) f.Delete();
                                }

                                this.Invoke(new Action(() => {
                                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[관제탑완료] {hub.UserName} 작업 완료 보고 수신! (새로운 작업 할당 대기)", 1, hub.RoomId);
                                }));
                            }

                            // =========================================================================
                            // 가장 중요한 UI 락 해제 및 타일 업데이트
                            // =========================================================================
                            this.Invoke(new Action(() =>
                            {
                                // [필수] 다음 작업을 받을 수 있도록 작업 중 목록에서 반드시 제거
                                if (lbJobs.Items.Contains(hub.UserName))
                                {
                                    lbJobs.Items.Remove(hub.UserName);
                                    작업상태아이콘업데이트();
                                }

                                if (서브컴작업시작시간.ContainsKey(hub.UserName))
                                {
                                    DateTime 시작시간 = 서브컴작업시작시간[hub.UserName];
                                    TimeSpan 최종경과 = DateTime.Now - 시작시간;
                                    int 최종건수 = 서브컴할당건수.ContainsKey(hub.UserName) ? 서브컴할당건수[hub.UserName] : 0;
                                    string 최종시간텍스트 = $"{(int)최종경과.TotalMinutes:D2}:{최종경과.Seconds:D2}";

                                    서브컴작업시작시간.Remove(hub.UserName);
                                    서브컴할당건수.Remove(hub.UserName);
                                    서브컴할당타임아웃.Remove(hub.UserName);

                                    string 번호문자열 = hub.UserName.Replace("sub", "").Replace("Ksub", "");
                                    if (int.TryParse(번호문자열, out int 인덱스))
                                    {
                                        if (인덱스 >= 1 && 인덱스 < 타일.Length && 타일[인덱스] != null)
                                        {
                                            타일[인덱스].Appearance.BackColor = Color.MediumPurple;
                                            타일[인덱스].Appearance.ForeColor = Color.White;
                                            string 꽉찬바 = new string('■', 10);
                                            string 완료상태바 = $"<color=#E6E6FA>{꽉찬바}</color> 100%";
                                            타일[인덱스].Text = $"<size=30>📄완료 : {최종건수:00}</size><br><size=10> </size><br><size=40><color=Cyan>🕒 {최종시간텍스트}</color></size><br><size=10> </size><br><size=18>{완료상태바}</size>";
                                        }
                                    }
                                }
                            }));
                        }
                        catch (Exception ex)
                        {
                            this.Invoke(new Action(() => {
                                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[관제탑오류] 완료 처리 중 오류 발생: {ex.Message}", 1, hub.RoomId);
                            }));
                        }
                    });
                }
            }





            //클라이언트만 해당
            //클라이언트는 sub컴에게 
            // =========================================================================
            // [1] 통신 오류 자동 복구 로직 (새 리스트 수신 시 발동)
            // =========================================================================
            if (실행주체 == "클라이언트" && hub.Message.StartsWith("[수신시작]") && hub.Message.Contains(".lst.sub"))
            {
                // 정상 종료를 했다면 '저장.출력폴더'가 null이므로 여기는 조건에 안 맞아 그냥 패스(무시)됩니다. 중복 실행 X!
                if (!string.IsNullOrEmpty(저장.출력폴더))
                {
                    try
                    {
                        string stateFile폴더 = $"{저장.출력폴더}StateFile\\";
                        if (Directory.Exists(stateFile폴더))
                        {
                            string 작업완료 = $"{stateFile폴더}모든작업종료.종료";
                            string 출력내용 = "[강제종료] 통신오류 자동복구";
                            File.WriteAllText(작업완료, 출력내용, Encoding.GetEncoding(949));
                        }

                        저장.출력폴더 = null; // ★ 여기서 비워주기 때문에 완벽함
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[상태복구] 누락된 이전 작업 강제 완료!", 1);
                    }
                    catch (Exception ex)
                    {
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[복구오류] {ex.Message}", 1);
                    }
                }
            }

            // =========================================================================
            // [2] 기존 정상 종료 로직 (서버로부터 완료 메세지 수신 시 발동)
            // =========================================================================
            if (hub.Message.EndsWith("*맞춤약관작업완료*"))
            {
                try
                {
                    // ★ [안전장치] 저번 답변에서 말씀드린 '놀고 있던 컴 에러 방지'를 위해 여기도 if 씌워줌
                    if (!string.IsNullOrEmpty(저장.출력폴더))
                    {
                        string stateFile폴더 = $"{저장.출력폴더}StateFile\\";
                        if (Directory.Exists(stateFile폴더))
                        {
                            string 작업완료 = $"{stateFile폴더}모든작업종료.종료";
                            string 출력내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[모든작업종료] : sub컴종료", DateTime.Now);
                            File.WriteAllText(작업완료, 출력내용, Encoding.GetEncoding(949));
                        }

                        저장.출력폴더 = null; // ★ 정상 처리 후 비워줌
                    }
                }
                catch (Exception ex)
                {
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[수신오류] 무시됨: {ex.Message}", 1);
                }
            }


            // =========================================================================
            // [서버 측 수신 이벤트] 클라이언트로부터 "[요청]강제초기화"를 받았을 때
            // =========================================================================
            if (실행주체 == "서버" && hub.Message == "[요청]강제초기화")
            {
                try
                {
                    string 감시폴더 = $"{저장.기본출력폴더}감시\\";
                    if (!Directory.Exists(감시폴더)) Directory.CreateDirectory(감시폴더);

                    string 신호파일 = System.IO.Path.Combine(감시폴더, "강제재시작.종료");

                    // 서버 로컬에 파일이 없다면 생성 (이후 하트비트가 알아서 전체 명령 내리고 초기화함)
                    if (!File.Exists(신호파일))
                    {
                        File.WriteAllText(신호파일, "CLIENT_REQUEST", Encoding.GetEncoding(949));
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템] 클라이언트({hub.UserName})의 강제초기화 요청 수신.", 1);
                    }
                }
                catch (Exception ex)
                {
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[오류] 강제초기화 요청 파일 생성 실패: {ex.Message}", 1);
                }
            }

            // =========================================================================
            // [클라이언트 측 수신 이벤트] 서버로부터 "[명령]전체재시작"을 받았을 때
            // =========================================================================
            if (실행주체 == "클라이언트" && hub.Message == "[명령]전체재시작")
            {
                try
                {
                    string 감시폴더 = $"{저장.기본출력폴더}감시\\";
                    if (!Directory.Exists(감시폴더)) Directory.CreateDirectory(감시폴더);

                    string 신호파일 = System.IO.Path.Combine(감시폴더, "강제재시작.종료");

                    // 서버의 명령에 따라 클라이언트 로컬에 파일 생성 (하트비트가 알아서 앱 끄고 청소함)
                    if (!File.Exists(신호파일))
                    {
                        File.WriteAllText(신호파일, "SERVER_COMMAND", Encoding.GetEncoding(949));
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템] 서버로부터 강제 초기화 명령 수신.", 1);
                    }
                }
                catch (Exception ex)
                {
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[오류] 전체재시작 명령 파일 생성 실패: {ex.Message}", 1);
                }
            }




        }

        // [H2PControl.cs 클래스 내부 - 재할당처리_비동기 메소드 전체]
        private async Task 재할당처리_비동기(string[] errorPks, string clientName)
        {
            try
            {
                // 1. 폴더 경로 정의
                string 빽업폴더 = @"d:\Easy약관Plus\Output\감시\TmpInput\";
                string 에러감시폴더 = $@"{저장.기본출력폴더}감시\오류\";

                if (!Directory.Exists(에러감시폴더))
                    Directory.CreateDirectory(에러감시폴더);

                if (!Directory.Exists(빽업폴더))
                {
                    this.Invoke(new Action(() => {
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[오류] TmpInput 백업 폴더가 존재하지 않습니다.", 1);
                    }));
                    return;
                }

                // 2. 발신 클라이언트 전용 파일만 검색 (예: clientName이 "sub3"이면 "*.sub3" 검색)
                string targetExtension = $"*.{clientName}";
                string[] targetFiles = Directory.GetFiles(빽업폴더, targetExtension);

                if (targetFiles.Length == 0)
                {
                    this.Invoke(new Action(() => {
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[오류] TmpInput 폴더 내에 {targetExtension} 전용 백업 파일이 없습니다.", 1);
                    }));
                    return;
                }

                // 3. 넘겨받은 오류 고객번호(PK)별로 복구 작업 진행
                foreach (string pk in errorPks)
                {
                    string 고객번호 = pk.Trim();
                    if (string.IsNullOrEmpty(고객번호)) continue;

                    // 실패 카운트 누적 (Strike)
                    if (!글로벌에러카운트.ContainsKey(고객번호)) 글로벌에러카운트[고객번호] = 0;
                    글로벌에러카운트[고객번호]++;
                    int 현재카운트 = 글로벌에러카운트[고객번호];

                    List<string> 추출된원본데이터 = new List<string>();

                    // 4. 타겟 파일(*.subX) 내부에서 해당 고객번호와 "정확히 일치"하는 데이터만 추출
                    await Task.Run(() =>
                    {
                        foreach (string 파일경로 in targetFiles)
                        {
                            if (!File.Exists(파일경로)) continue;

                            string[] 모든줄 = File.ReadAllLines(파일경로, Encoding.GetEncoding(949));
                            foreach (string 한줄 in 모든줄)
                            {
                                if (string.IsNullOrWhiteSpace(한줄)) continue; // 빈 줄 무시

                                // 구분자('|')로 잘라서 배열로 만듦
                                string[] 데이터배열 = 한줄.Split('|');

                                // 0번째 데이터가 존재하고, 그 값이 찾고자 하는 고객번호와 "완벽히 일치(==)" 할 때만 담음
                                if (데이터배열.Length > 0 && 데이터배열[0].Trim() == 고객번호)
                                {
                                    추출된원본데이터.Add(한줄);
                                    // 한 고객의 데이터가 한 줄로 끝난다면 여기서 break를 걸어도 되지만,
                                    // 혹시 여러 줄로 구성될 가능성을 대비해 동일 파일 내의 모든 줄을 끝까지 검사합니다.
                                }
                            }

                            // 이 파일에서 고객 데이터를 찾았다면, 다른 날짜나 다른 백업 파일들은 더 이상 뒤지지 않고 탐색 종료
                            if (추출된원본데이터.Count > 0) break;
                        }
                    });

                    if (추출된원본데이터.Count == 0)
                    {
                        this.Invoke(new Action(() => {
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[경고] {targetExtension} 백업 파일에서 고객번호({고객번호})를 찾을 수 없습니다.", 1);
                        }));
                        continue;
                    }

                    // 5. 3아웃 영구 격리 vs 재할당 배포
                    if (현재카운트 >= 3)
                    {
                        // [3아웃 영구 격리]
                        string 격리파일명 = $@"{에러감시폴더}영구격리_#KB_{고객번호}_{DateTime.Now:HHmmss}.lst";
                        await Task.Run(() => File.WriteAllLines(격리파일명, 추출된원본데이터, Encoding.GetEncoding(949)));

                        this.Invoke(new Action(() => {
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[영구격리] 고객번호: {고객번호} (3회 실패로 격리 폴더 이동)", 1);
                        }));
                    }
                    else
                    {
                        // [재할당 배포] 원본 Input 폴더에 새 리스트로 드랍 -> 관제탑이 즉시 타 컴으로 토스
                        string 재할당파일명 = $@"{저장.기본파일폴더}#KB_재할당_{고객번호}_Err{현재카운트}_{DateTime.Now:HHmmssFFFF}.lst";
                        await Task.Run(() => File.WriteAllLines(재할당파일명, 추출된원본데이터, Encoding.GetEncoding(949)));

                        this.Invoke(new Action(() => {
                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[재할당배포] 고객번호: {고객번호} (실패 {현재카운트}회 / {targetExtension} 복구) -> Input 재투입", 1);
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() => {
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[재할당처리오류] 파일 기반 재분배 중 오류: {ex.Message}", 1);
                }));
            }
        }
        private void S_Connected(object sender, ChatEventArgs e)
        {

            if (접속컴.ContainsKey(e.Hub.UserName))
            {
                // 1. UI 리스트박스에서 기존 이름 삭제 (2개 중복 표시 방지)
                for (int i = lbxClients.Items.Count - 1; i >= 0; i--)
                {
                    if (lbxClients.Items[i].ToString() == e.Hub.UserName)
                    {
                        lbxClients.Items.RemoveAt(i);
                    }
                }

                // 2. 딕셔너리에서 기존 정보 삭제
                접속컴.Remove(e.Hub.UserName);

                // 3. (중요) 실제 통신을 담당하는 _roomManager에서도 기존 소켓 삭제
                _roomManager.RemoveOldClient(e.Hub.UserName); 
            }

            var hub = CreateNewStateChatHub(e.Hub, ChatState.Connect);

            //RoomId 키값으로 딕셔너리에 TCP/IP정보 저장
            _roomManager.Add(e.ClientHandler);
            _roomManager.SendToMyRoom(hub);

            //메세지출력
            lbxClients.Items.Add($"{e.Hub.UserName}");
            AddClientMessageList(hub);

            if (!접속컴.ContainsKey(e.Hub.UserName))
                접속컴.Add(e.Hub.UserName, e.Hub.RoomId);

            lbxClients_Display(e.Hub.RoomId);
        }
        private void S_Disconnected(object sender, ChatEventArgs e)
        {
            var hub = CreateNewStateChatHub(e.Hub, ChatState.Disconnect);

            _roomManager.Remove(e.ClientHandler);
            _roomManager.SendToMyRoom(hub);

            lbxClients.Items.Remove($"{e.Hub.UserName}");
            lbJobs.Items.Remove($"{e.Hub.UserName}");
            작업상태아이콘업데이트();

            AddClientMessageList(hub);
            접속컴.Remove(e.Hub.UserName);
            lbxClients_Display(e.Hub.RoomId);

            // =========================================================================
            // 관제탑 모드: 서브컴이 예기치 않게 연결이 끊기면 기다리지 말고 즉시 데이터 회수!
            // =========================================================================
            if (실행주체 == "서버" && chk싱글처리.Checked && 분산메인컴)
            {
                string 끊긴컴 = e.Hub.UserName;
                string tmpInputPath = $@"{저장.기본출력폴더}감시\TmpInput\";

                if (Directory.Exists(tmpInputPath))
                {
                    FileInfo[] files = new DirectoryInfo(tmpInputPath).GetFiles($"*.{끊긴컴}");
                    foreach (FileInfo backupFile in files)
                    {
                        로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[관제탑] {끊긴컴} 연결 끊김 감지! 대기 없이 즉시 데이터를 회수합니다.", 1);

                        try
                        {
                            // 1. 끊긴 컴이 들고 있던 데이터 구출
                            string[] 구출데이터 = File.ReadAllLines(backupFile.FullName, Encoding.GetEncoding(949));
                            if (구출데이터.Length > 0)
                            {
                                int hashIndex = backupFile.Name.IndexOf('#');
                                string 고객사 = "CCK";
                                if (hashIndex != -1 && backupFile.Name.Length > hashIndex + 3)
                                    고객사 = backupFile.Name.Substring(hashIndex + 1, 2);

                                // 다음 분배 루프에서 바로 다시 가져갈 수 있도록 Input 폴더로 반환
                                string 복구파일명 = $"{저장.기본파일폴더}#{고객사}_즉시복구_{DateTime.Now:HHmmssFFFF}.lst";
                                File.WriteAllLines(복구파일명, 구출데이터, Encoding.GetEncoding(949));
                            }
                        }
                        catch { }

                        // 2. TmpInput 찌꺼기 파일 삭제 (이제 재접속하면 다시 일을 받을 수 있음)
                        try { backupFile.Delete(); } catch { }
                    }
                }

                // =========================================================================
                // ★ [추가] 대시보드(타일) 타이머 메모리 해제 및 '대기' 화면 초기화
                // =========================================================================
                this.Invoke(new Action(() =>
                {
                    if (서브컴작업시작시간.ContainsKey(끊긴컴))
                    {
                        // 1. 타이머 계산을 위한 딕셔너리에서 흔적 지우기
                        서브컴작업시작시간.Remove(끊긴컴);
                        서브컴할당건수.Remove(끊긴컴);
                        서브컴할당타임아웃.Remove(끊긴컴);

                        // 2. 해당 서브컴의 대시보드 인덱스 추출 (sub1 -> 1)
                        string 번호문자열 = 끊긴컴.Replace("sub", "").Replace("Ksub", "");
                        if (int.TryParse(번호문자열, out int 인덱스))
                        {
                            if (인덱스 >= 1 && 인덱스 < 타일.Length && 타일[인덱스] != null)
                            {
                                // 3. 타일 색상 및 텍스트를 초기 기본(회색 대기) 상태로 복구
                                타일[인덱스].Appearance.BackColor = Color.DimGray;
                                타일[인덱스].Appearance.ForeColor = Color.White;

                                string 빈상태바 = "<color=#D3D3D3>□□□□□□□□□□</color> 0%";
                                타일[인덱스].Text = $"<size=30>📄대기 : 00</size><br><size=10> </size><br><size=40><color=Silver>🕒 00:00</color></size><br><size=10> </size><br><size=18>{빈상태바}</size>";
                            }
                        }
                    }
                }));
            }

        }

        private void S_Received(object sender, ChatEventArgs e)
        {
            _roomManager.SendToMyRoom(e.Hub);

            AddClientMessageList(e.Hub);

            if (e.Hub.State == ChatState.Ack) // 서버에서 클라이언트로 파일수신완료
            {
                string 종료확장자 = new FileInfo(e.Hub.FileName).Extension.ToLower();
                if (종료확장자 == ".end")
                {
                    lbJobs.Items.Remove($"{e.Hub.UserName}");
                    작업상태아이콘업데이트();
                }
                    
            }
        }



        // 폼이 위치한 현재 모니터의 이름을 저장하기 위한 변수
        private string currentScreenDeviceName = "";
        private void lbxClients_Display(int ID)
        {
            // ★ 소켓 통신 스레드에서 UI 크기를 변경하므로 안전하게 Invoke 처리
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => lbxClients_Display(ID)));
                return;
            }

            // 1. 현재 접속된 클라이언트 중 가장 높은 ID(RoomId) 찾기
            int maxRoomId = 0;
            if (접속컴.Count > 0)
            {
                maxRoomId = 접속컴.Values.Max();
            }
            maxRoomId = Math.Min(5, maxRoomId);

            // 2. 리스트박스 및 타일 가시성(Visible) 업데이트
            if (리스트 != null)
            {
                for (int i = 1; i <= 5; i++)
                {
                    bool isVisible = (i <= maxRoomId);

                    if (i < 리스트.Length && 리스트[i] != null)
                    {
                        리스트[i].Visible = isVisible;
                    }

                    if (타일 != null && i < 타일.Length && 타일[i] != null)
                    {
                        타일[i].Visible = isVisible; // 타일 대시보드도 같이 숨기거나 표시
                    }
                }
            }

            // ====================================================================
            // ★ 3. [핵심] 서브컴 접속 수에 맞춰 폼 가로 사이즈 다이내믹 확장
            // ====================================================================
            if (분산메인컴)
            {
                System.Drawing.Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
                int baseWidth = 518; // 서버 영역 고정 너비

                // 모니터에서 서브컴들이 사용할 수 있는 남은 영역을 5등분하여 '1대당 차지할 칸 너비'를 구합니다.
                int unitWidth = (workingArea.Width - baseWidth) / 5;

                // 최종 너비 = 기본 서버너비 + (1대당 너비 * 접속된 서브컴 수)
                int targetWidth = baseWidth + (unitWidth * maxRoomId);

                // 폼 크기를 변경합니다. (크기가 변하면 H2PControl_Resize가 자동으로 발동하여 내부 리스트가 예쁘게 분할 정렬됩니다!)
                this.Width = targetWidth;
            }
            else
            {
                H2PControl_Resize(this, EventArgs.Empty);
            }
        }

        private void H2PControl_LocationChanged(object sender, EventArgs e)
        {
            // 폼이 최소화된 상태에서는 계산하지 않음
            if (this.WindowState == FormWindowState.Minimized) return;

            // === [추가] 폼 이동 시 타이틀 갱신 ===
            타이틀업데이트();

            Screen currentScreen = Screen.FromControl(this);

            // 폼이 다른 모니터로 넘어갔을 때만 재배치 로직 실행
            if (currentScreen.DeviceName != currentScreenDeviceName)
            {
                // 모니터 변경 감지 완료. 레이아웃 재계산을 위해 기존 메서드 호출
                int maxId = 0;
                if (접속컴.Count > 0)
                {
                    maxId = 접속컴.Values.Max();
                }
                lbxClients_Display(maxId);
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            this.Close();
            
        }

        private void RunningStateChanged(bool isRunning)
        {
            BtnStop.Enabled = isRunning;
        }
        

        // =============================================================================
        //  TCP/IP (Client)
        // =============================================================================

        private void C_Connected(object sender, ChatEventArgs e)
        {
            _clientHandler = e.ClientHandler;


            // ✅ 서버와 연결될 때 단 한 번만 이벤트를 등록합니다.
            _clientHandler.FileProgressChanged += (fileName, progress) =>
            {
                this.Invoke(new Action(() =>
                {
                    progressBar.EditValue = (int)Math.Min(100, progress);
                    lblProgress.Text = $"{fileName} : {progress:F1}%";
                    string 로그 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[수신] {fileName} : {progress:F1}%";
                    로그기록(로그, 1);
                }));
            };

            _clientHandler.FileStatusChanged += (fileName, status) =>
            {
                this.Invoke(new Action(() =>
                {
                    string 로그 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[시작] {fileName}";
                    로그기록(로그, 1);
                }));
            };

            _clientHandler.FileSendCompleted += (fileName, 파일cnt) =>
            {
                this.Invoke(new Action(() =>
                {
                    string 로그 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[완료] {fileName} : count - {파일cnt}";
                    로그기록(로그, 1);
                }));
            };


        }

        private async void C_Disconnected(object sender, ChatEventArgs e)
        {
            _clientHandler = null;
            로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[연결끊김] 서버와 연결이 끊겼습니다.";
            로그기록(로그내용,1);

            if (isRestarting)
            {
                로그내용 = $"{DateTime.Now:yy.MM.dd HH:mm:ss}[안내] 앱 재시작 과정이므로 자동 재접속을 생략합니다.";
                로그기록(로그내용, 1);
                return; 
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
            _ = await TCPServer_Client초기화(실행주체, "시작", 설정);
        }

        private void C_Received(object sender, ChatEventArgs e)
        {
            //클라이언트에서 서버로 파일을 송신하면 서버쪽에서 파일을 다 수신 후 클라이언트쪽으로 보내는 상태값
            if (e.Hub.State == ChatState.Ack)
            {
                this.Invoke(new Action(() =>
                {
                    // ProgressBar를 100%로 표시
                    progressBar.EditValue = 100;
                }));
            }
            AddClientMessageList(e.Hub);
        }

      

        // =============================================================================
        //  앱종료(공통)
        // =============================================================================
        private bool isClosing = false; // 종료 작업이 진행 중인지 체크하는 플래그
        private async void H2PControl_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!isClosing)
            {
                e.Cancel = true;  // 핵심: 창이 바로 닫히는 것을 막음 (메인 프로세스 생존 보장)
                isClosing = true; // 종료 작업 시작 플래그 ON

                this.Enabled = false; // 종료 중 사용자가 다른 버튼을 못 누르도록 폼 비활성화 (선택 사항)

                await F1종료모두종료();

                // 3. F1~F8 앱이 모두 죽은 것을 확인했으니, 이제 진짜로 폼을 닫습니다.
                this.Close();
            }
        }


        private async Task F1종료모두종료()
        {
            // ★ 핵심: 감시 루프들이 개별적으로 앱을 다시 살리지 못하도록 잠금장치를 먼저 겁니다.
            lock (_circuitBreakerLock)
            {
                F앱전체초기화중 = true;
            }

            try
            {
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[수동종료] 사용자에 의해 F1~F8 전체 강제 종료를 시작합니다.", 1);

                // 8개 앱 강제 종료 작업 (병렬 처리)
                var tasks = new List<Task>();
                for (int i = 1; i <= 8; i++)
                {
                    UpdateFProcessState(i - 1, "WAITING"); // LED 이미지 끄기
                    tasks.Add(F1프로그램종료(i));
                }

                // 모든 앱이 꺼질 때까지 대기
                await Task.WhenAll(tasks);

                Hwp프로세스싹쓸이();

                // 윈도우 자원 정리를 위해 잠시 대기
                await Task.Delay(3000);

                // ★ 핵심: 수동 종료 중 쌓였을지 모를 에러 기록을 깨끗이 비워줍니다.
                while (F앱에러시간기록.TryDequeue(out _)) { }
                최근GlobalFlush시간 = DateTime.Now;
            }
            catch (Exception ex)
            {
                로그기록($"[수동종료오류] {ex.Message}", 1);
            }
            finally
            {
                // ★ 핵심: 이제 청소가 끝났으니 감시 루프들이 앱을 다시 살릴 수 있게 잠금을 풉니다.
                F앱전체초기화중 = false;
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[수동종료완료] 전체 초기화 후 다시 작업을 시작합니다.", 1);
            }
        }


        private async void simpleButton1_Click(object sender, EventArgs e)
        {
            if (!isClosing && !F앱전체초기화중) // 이미 초기화 중이면 중복 실행 방지
            {
                await F1종료모두종료();
            }
        }

        private async void F1앱모두종료_Click(object sender, EventArgs e)
        {
            if (isClosing || F앱전체초기화중) return;

            await F1종료모두종료();
        }

        private void bntRestart_Click(object sender, EventArgs e)
        {
            string 감시폴더 = $"{저장.기본출력폴더}감시\\";
            string 신호파일 = System.IO.Path.Combine(감시폴더, "강제재시작.종료");

            if (!File.Exists(신호파일))
            {
                File.WriteAllText(신호파일, "TIMEOUT_SERVER", Encoding.GetEncoding(949));
            }

        }

        private void btn작업대기_Click(object sender, EventArgs e)
        {
            _isRunning = false;
            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템] 프로그램 종료 대기 중... 진행 중인 작업이 완료되면 멈춥니다.", 1);
        }


        private System.Windows.Forms.Timer 통약관MonitorTimer;
        private bool is통약관Restarting = false;

        // ==========================================================================================================
        // ★ [이관 완료] Easy전체약관(통약관) 감시 및 자동 재시작 로직 (관제탑 전용)
        // ==========================================================================================================
        private void Start통약관Monitoring()
        {
            string 실행파일명 = "Easy전체약관.exe";
            string 실행경로 = @"c:\Easy약관Plus\program\Easy전체약관\";
            string 실행파일전체경로 = System.IO.Path.Combine(실행경로, 실행파일명);

            string 프로세스이름 = System.IO.Path.GetFileNameWithoutExtension(실행파일명);
            bool isRunning = Process.GetProcessesByName(프로세스이름).Length > 0;

            // 1. 처음 켜질 때 죽어있으면 1회 살려줌
            if (!isRunning)
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = 실행파일전체경로,
                        WorkingDirectory = 실행경로,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };
                    Process.Start(startInfo);
                    this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [시스템] 통약관 앱 최초 실행 완료.", 1)));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [ERROR] 통약관 최초 실행 실패: {ex.Message}", 1)));
                }
            }

            // 2. 1분 주기 감시 타이머 시작
            if (통약관MonitorTimer == null)
            {
                통약관MonitorTimer = new System.Windows.Forms.Timer();
                통약관MonitorTimer.Interval = 30000; // 30초
                통약관MonitorTimer.Tick += 통약관MonitorTimer_Tick;
            }
            통약관MonitorTimer.Start();
        }

        private async void 통약관MonitorTimer_Tick(object sender, EventArgs e)
        {
            if (is통약관Restarting) return;
            if (isRestarting || F앱전체초기화중) return; // ★ [추가] 관제탑이 대청소 중일 때는 통약관도 건드리지 않고 대기!

            string statusPath = @"d:\Easy약관Plus\Output\STATUS\앱감시상태2.txt";
            string 실행파일명 = "Easy전체약관.exe";
            string 실행경로 = @"c:\Easy약관Plus\program\Easy전체약관\";
            string 실행파일전체경로 = System.IO.Path.Combine(실행경로, 실행파일명);
            bool needsRestart = false;

            try
            {
                string 프로세스이름 = System.IO.Path.GetFileNameWithoutExtension(실행파일명);
                bool isRunning = Process.GetProcessesByName(프로세스이름).Length > 0;

                // 물리적으로 꺼졌거나, 3분(180초) 동안 갱신이 없으면 재시작 판정
                if (!isRunning)
                {
                    needsRestart = true;
                }
                else
                {
                    if (File.Exists(statusPath))
                    {
                        DateTime lastTime = File.GetLastWriteTime(statusPath);
                        if ((DateTime.Now - lastTime).TotalSeconds > 65)
                        {
                            needsRestart = true;
                        }
                    }
                    else
                    {
                        needsRestart = true;
                    }
                }
            }
            catch { }

            // 심폐소생(Kill & Start) 실행
            if (needsRestart)
            {
                is통약관Restarting = true;
                통약관MonitorTimer.Stop();

                try
                {
                    // 1. 기존 좀비 프로세스 싹쓸이
                    string 프로세스이름 = System.IO.Path.GetFileNameWithoutExtension(실행파일명);
                    Process[] processes = Process.GetProcessesByName(프로세스이름);
                    foreach (Process p in processes)
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                    }
                }
                catch { }

                await Task.Delay(2000);
                try { if (File.Exists(statusPath)) File.Delete(statusPath); } catch { }

                try
                {
                    // 2. 새 프로세스 띄우기
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = 실행파일전체경로,
                        WorkingDirectory = 실행경로,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };
                    Process.Start(startInfo);
                    this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [시스템] 통약관 감시: 65초 응답없음. 강제 재시작 완료.", 1)));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [ERROR] 통약관 재시작 실패: {ex.Message}", 1)));
                }

                await Task.Delay(3000); // 부팅 여유시간
                is통약관Restarting = false;
                통약관MonitorTimer.Start();
            }
        }

        // =========================================================================
        // ★ [추가] 날짜 변경 시 오래된 폴더 자동 삭제 (데이터 3일 / 로그 30일)
        // =========================================================================
        private async Task 오래된파일정리_비동기()
        {
            // 관제탑 메인 루프에 렉을 유발하지 않도록 백그라운드 스레드에서 조용히 실행합니다.
            await Task.Run(() =>
            {
                try
                {
                    // ==========================================================
                    // [1부] 일반 데이터 파일 정리 (기준: 3일 경과)
                    // ==========================================================
                    DateTime 데이터기준일 = DateTime.Now.Date.AddDays(-3);
                    List<string> 거래처목록 = new List<string>() { "CCZ", "CCA", "CCK", "CCD" };
                    List<string> 대상최상위폴더들 = new List<string>();

                    foreach (string 거래처 in 거래처목록)
                    {
                        대상최상위폴더들.Add($@"d:\Easy약관Plus\List\@완료\{거래처}\");
                        대상최상위폴더들.Add($@"d:\Easy약관Plus\Input\@완료\{거래처}\");
                        대상최상위폴더들.Add($@"d:\Easy약관Plus\Output\{거래처}\");
                    }

                    foreach (string 최상위폴더 in 대상최상위폴더들)
                    {
                        if (Directory.Exists(최상위폴더))
                        {
                            DirectoryInfo dirInfo = new DirectoryInfo(최상위폴더);
                            foreach (DirectoryInfo 월폴더 in dirInfo.GetDirectories())
                            {
                                foreach (DirectoryInfo 일폴더 in 월폴더.GetDirectories())
                                {
                                    if (DateTime.TryParseExact($"{월폴더.Name}.{일폴더.Name}", "yyyy.MM.dd", null, System.Globalization.DateTimeStyles.None, out DateTime 폴더날짜))
                                    {
                                        if (폴더날짜.Date < 데이터기준일)
                                        {
                                            try { 일폴더.Delete(true); } catch { }
                                        }
                                    }
                                }
                                if (월폴더.GetDirectories().Length == 0 && 월폴더.GetFiles().Length == 0)
                                {
                                    try { 월폴더.Delete(true); } catch { }
                                }
                            }
                        }
                    }

                    // ==========================================================
                    // [2부] 시스템 로그 파일 정리 (기준: 30일 경과)
                    // ==========================================================
                    DateTime 로그기준일 = DateTime.Now.Date.AddDays(-30);
                    string 로그최상위폴더 = @"c:\Easy약관Plus\program\LogFile\";

                    if (Directory.Exists(로그최상위폴더))
                    {
                        DirectoryInfo logDirInfo = new DirectoryInfo(로그최상위폴더);
                        foreach (DirectoryInfo 월폴더 in logDirInfo.GetDirectories())
                        {
                            foreach (DirectoryInfo 일폴더 in 월폴더.GetDirectories())
                            {
                                if (DateTime.TryParseExact($"{월폴더.Name}.{일폴더.Name}", "yyyy.MM.dd", null, System.Globalization.DateTimeStyles.None, out DateTime 폴더날짜))
                                {
                                    if (폴더날짜.Date < 로그기준일) // 30일이 지난 폴더만 타겟팅
                                    {
                                        try
                                        {
                                            일폴더.Delete(true);
                                            로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템청소] 30일 경과 로그 폴더 삭제 완료: {일폴더.FullName}", 1);
                                        }
                                        catch { }
                                    }
                                }
                            }
                            // 일(Day) 폴더가 다 지워져서 텅 빈 월(Month) 폴더 삭제
                            if (월폴더.GetDirectories().Length == 0 && 월폴더.GetFiles().Length == 0)
                            {
                                try { 월폴더.Delete(true); } catch { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss}[시스템청소 오류] {ex.Message}", 1);
                }
            });
        }


        // ==========================================================================================================
        // ★ [STEP 4] 메인 프로그램의 '병합 지시'를 감시하는 루프
        // ==========================================================================================================
        private async Task 최종병합감시루프(CancellationToken token)
        {
            string 감시폴더 = $@"{저장.기본출력폴더}감시\";
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (Directory.Exists(감시폴더))
                    {
                        foreach (FileInfo file in new DirectoryInfo(감시폴더).GetFiles("*.병합지시"))
                        {
                            // 1. 병합 지시 파일 읽기
                            string 내용 = File.ReadAllText(file.FullName, Encoding.GetEncoding(949));
                            string[] 파라미터 = 내용.Split('|');
                            if (파라미터.Length >= 3)
                            {
                                string 고객사 = 파라미터[0];
                                string groupKey = 파라미터[1];
                                string 출력폴더 = 파라미터[2];

                                this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [최종병합] {groupKey} 병합지시 수신. 작업을 시작합니다.", 1)));

                                // 2. 지시 파일 삭제 (중복 실행 방지)
                                file.Delete();

                                // 3. 실제 병합 로직 실행
                                await 최종증권약관병합_실행(고객사, groupKey, 출력폴더);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [병합감시 오류] {ex.Message}", 1)));
                }
                await Task.Delay(3000, token);
            }
        }

        // ==========================================================================================================
        // ★ [STEP 4] 지시를 받아 DB를 읽고 PdfManager를 구동하는 실제 실행부
        // ==========================================================================================================
        // ==========================================================================================================
        // ★ [STEP 4] 지시를 받아 DB를 읽고 PdfManager를 구동하는 실제 실행부
        // ==========================================================================================================
        // ==========================================================================================================
        // ★ [STEP 4] 지시를 받아 DB를 읽고 PdfManager를 구동하는 실제 실행부
        // ==========================================================================================================
        private async Task 최종증권약관병합_실행(string 고객사, string groupKey, string 출력폴더)
        {
            string 약관입력폴더 = $"{출력폴더}Y\\";
            string 증권입력폴더 = string.Format("{0}@증권\\{1}\\", 저장.기본입력폴더, 고객사);

            Dictionary<string, string> 증권목록 = new Dictionary<string, string>();
            Dictionary<string, string> 약관목록 = new Dictionary<string, string>();

            if (Directory.Exists(증권입력폴더))
            {
                foreach (FileInfo 파일 in new DirectoryInfo(증권입력폴더).GetFiles("*.pdf"))
                {
                    증권목록.Add(System.IO.Path.GetFileNameWithoutExtension(파일.Name), 파일.FullName);
                }
            }
            if (Directory.Exists(약관입력폴더))
            {
                foreach (FileInfo 파일 in new DirectoryInfo(약관입력폴더).GetFiles("Y*.pdf"))
                {
                    약관목록.Add(System.IO.Path.GetFileNameWithoutExtension(파일.Name).Substring(1), 파일.FullName);
                }
            }

            // 1. DB에서 작업해야 할 레코드 정보 읽기 (메인 폼의 레코드s 객체를 대체)
            List<dynamic> 로컬레코드 = new List<dynamic>();
            using (SqlConnection conn = new SqlConnection(설정.dataTable접속))
            {
                await conn.OpenAsync();
                string sql = $"SELECT * FROM data WHERE groupKey = '{groupKey}' AND 개별PDF작업 <> '개별PDF완료' ORDER BY 고객Key";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string[] 분리 = reader["data"].ToString().Split('|').Select(P => P.Trim()).ToArray();
                        string 파일명 = reader["파일명"].ToString();
                        string 전체약관경로 = reader["전체약관경로"]?.ToString() ?? "";

                        // PdfManager가 사용할 규격에 맞춰 동적 객체(Dynamic) 생성
                        var rec = new
                        {
                            고객번호 = 분리[0],
                            고객명 = 분리[4],
                            우편번호 = 분리[7],
                            주소 = 분리[8],
                            서식구분 = 분리[10],
                            발송구분 = 분리[11],
                            Password = 분리[12],
                            tmp03 = 분리.Length > 14 ? 분리[14] : "",
                            고객사 = 고객사,
                            파일명 = 파일명,
                            전체약관경로 = 전체약관경로,
                            출력폴더 = 출력폴더
                        };
                        로컬레코드.Add(rec);
                    }
                }
            }

            var 완료고객번호 = new ConcurrentBag<string>();
            var 오류고객번호 = new ConcurrentBag<string>();
            int[,] 전체집계 = new int[12, 10];
            object 집계락 = new object();
            int 정상cnt = 0;
            int 분류Pcnt = 0, 분류Mcnt = 0, 분류Ecnt = 0, 분류Lcnt = 0;

            var semaphore = new SemaphoreSlim(8);
            var tasks = 로컬레코드.Select(async 레코드 =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // dynamic 객체의 속성들을 미리 string으로 캐스팅하여 컴파일러 에러 방지
                    string 레코드서식구분 = (string)레코드.서식구분;
                    string 레코드발송구분 = (string)레코드.발송구분;
                    string 레코드전체약관경로 = (string)레코드.전체약관경로;
                    string 레코드고객번호 = (string)레코드.고객번호;
                    string 레코드파일명 = (string)레코드.파일명;
                    string 레코드출력폴더 = (string)레코드.출력폴더;

                    bool 증권만 = 레코드서식구분 == "03" || 레코드서식구분 == "3";
                    bool 증권약관 = 레코드서식구분 == "01" || 레코드서식구분 == "1";

                    string 메세지 = "";
                    int[,] 개별집계 = null;

                    if (!string.IsNullOrWhiteSpace(레코드전체약관경로))
                    {
                        메세지 = "성공";
                    }
                    else
                    {
                        if (!증권만 && !약관목록.TryGetValue(레코드파일명, out string 약관파일))
                        {
                            오류고객번호.Add(레코드고객번호); return;
                        }
                        if (증권약관 && !증권목록.TryGetValue(레코드고객번호, out string 증권파일))
                        {
                            오류고객번호.Add(레코드고객번호); return;
                        }

                        // ★ [가장 중요한 부분] 완벽하게 격리된 PdfManager 모듈 호출
                        현재상태 임시상태 = new 현재상태() { 출력폴더 = 레코드출력폴더, 고객사 = 고객사, groupKey = groupKey, dataTable접속 = 설정.dataTable접속 };

                        Action<string> logAction = (msg) => { this.Invoke(new Action(() => 로그기록(msg, 1))); };

                        증권목록.TryGetValue(레코드고객번호, out string zf);
                        약관목록.TryGetValue(레코드파일명, out string yf);

                        // === CS1061 해결: H2PControl에는 전역 설정값이 없으므로 빈 딕셔너리를 생성해서 넘김 ===
                        var result = await PdfManager.개발증권약관병합_PME분리(
                            레코드,
                            zf ?? "",
                            yf ?? "",
                            임시상태,
                            new Dictionary<string, string>() // <--- 수정됨
                        );
                        메세지 = result.메세지;
                        개별집계 = result.집계;
                    }

                    if (메세지 == "성공" || 메세지 == "북마크")
                    {
                        완료고객번호.Add(레코드고객번호);
                        Interlocked.Increment(ref 정상cnt);
                        switch (레코드발송구분)
                        {
                            case "01": case "1": Interlocked.Increment(ref 분류Pcnt); break;
                            case "02": case "2": Interlocked.Increment(ref 분류Mcnt); break;
                            case "03": case "3": Interlocked.Increment(ref 분류Ecnt); break;
                            case "04": case "4": Interlocked.Increment(ref 분류Lcnt); break;
                        }
                        lock (집계락)
                        {
                            if (개별집계 != null)
                            {
                                for (int r = 0; r < 12; r++)
                                    for (int c = 0; c < 10; c++)
                                        전체집계[r, c] += 개별집계[r, c];
                            }
                        }
                        this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [완료] 증권 + 약관병합 완료 - 고객번호 : {레코드고객번호}", 1)));
                    }
                    else
                    {
                        오류고객번호.Add(레코드고객번호);
                        this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [오류] 증권 + 약관병합 오류 - 고객번호 : {레코드고객번호}, {메세지}", 1)));
                    }
                }
                catch (Exception ex)
                {
                    오류고객번호.Add((string)레코드.고객번호);
                    this.Invoke(new Action(() => 로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [예외] {(string)레코드.고객번호} 처리 중 오류: {ex.Message}", 1)));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // 2. 집계 파일 생성
            List<string> 집계타이틀 = new List<string>() { "", "P건수", "P장수", "M건수", "M장수", "E건수", "E장수", "L건수", "L장수", "T건수", "T장수" };
            string 집계파일 = string.Format("{0}@맞춤증권\\({1:yyMMdd_HHmmss})집계.TOT", 출력폴더, DateTime.Now);
            using (FileStream fs집계 = new FileStream(집계파일, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write))
            using (StreamWriter lst집계 = new StreamWriter(fs집계, Encoding.GetEncoding(949)))
            {
                lst집계.WriteLine("========================================================================================");
                lst집계.WriteLine("            전체        증권+약관   약관만      증권만      상세증권    상세약관");
                lst집계.WriteLine("========================================================================================");
                for (int r = 1; r <= 10; r++)
                {
                    var values = Enumerable.Range(0, 6).Select(c => 전체집계[r, c].ToString().PadRight(10, ' '));
                    lst집계.WriteLine(집계타이틀[r].PadRight(10, ' ') + string.Join("\t", values));
                }
            }

            // 3. DB 업데이트 (메인 폼에서 하던 Update를 여기서 직접 수행)
            현재상태 DB상태 = new 현재상태() { groupKey = groupKey, 처리구분 = 설정.처리구분, dataTable접속 = 설정.dataTable접속 };

            // === CS1501 해결: 인수 3개만 넘기도록 수정됨 ===
            await BatchUpdateAsync(DB상태, 완료고객번호, 오류고객번호);

            // 4. 완료 파일 생성 (메인 프로그램의 대기 락(Lock)을 풀어줍니다)
            string 완료파일 = $"{저장.기본출력폴더}감시\\{groupKey}.병합완료";
            File.WriteAllText(완료파일, "최종 병합 완료", Encoding.GetEncoding(949));

            this.Invoke(new Action(() => {
                로그기록($"{DateTime.Now:yy.MM.dd HH:mm:ss} [최종병합] {groupKey} 작업이 모두 완료되어 메인 앱으로 신호를 보냅니다.", 1);
            }));
        }



    }






    // ========================================================================================================================================================

    public class PreventSleep
    {
        // 윈도우 API 불러오기
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern uint SetThreadExecutionState(uint esFlags);

        // 플래그 상수 정의
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001; // 시스템 잠김 방지
        private const uint ES_DISPLAY_REQUIRED = 0x00000002; // 화면 꺼짐 방지

        /// <summary>
        /// 절전 모드 및 화면 보호기 실행을 막습니다. (작업 시작 시 호출)
        /// </summary>
        public static void DonSleep()
        {
            // 시스템이 잠기거나 화면이 꺼지는 것을 막음
            // HWP 같은 GUI 자동화는 화면이 꺼지면 에러가 날 수 있으므로 Display도 켜둬야 함
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        }

        /// <summary>
        /// 다시 절전 모드가 가능하도록 설정을 해제합니다. (프로그램 종료 시 호출)
        /// </summary>
        public static void AllowSleep()
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }


        


    }

    public class 세션파일
    {
        public string PK { get; set; }
        public string 경로 { get; set; }
    }
    public class 저장
    {
        public static string 약관DB;
        public static string DB설정파일path;
        public static string 기본입력폴더;
        public static string 기본출력폴더;
        public static string 기본파일폴더;
        public static string 사용컴퓨터;
        public static string 로그경로;
        public static string 출력폴더;

    }
    public class 현재상태
    {
        public string 처리Step { get; set; } = string.Empty;
        public string 상태컴퓨터 { get; set; } = string.Empty;
        public string dataTable접속 { get; set; } = string.Empty;
        public int 병렬컴 { get; set; }
        public string 처리구분 { get; set; } = string.Empty;
        public string groupKey { get; set; } = string.Empty;
        public string 고객사 { get; set; } = string.Empty;
        public int 앱실행수 { get; set; }
        public string 출력폴더 { get; set; } = string.Empty;
        public string 입력폴더 { get; set; } = string.Empty;
        public bool 병렬처리여부 { get; set; }
        public string 접속IP { get; set; } = string.Empty;
        public int  접속PORT { get; set; }
        public List<string> 작업참여클라이언트 { get; set; } = new List<string>();
        public 현재상태() { }
        public List<string> 오류고객목록 { get; set; } = new List<string>();

        public 현재상태(환경설정 설정)
        {
            this.병렬처리여부 = 설정.병렬처리여부;
            this.상태컴퓨터 = 설정.상태컴퓨터;
            this.처리구분 = 설정.처리구분;
            this.dataTable접속 = 설정.dataTable접속;
            this.접속IP = 설정.접속IP;
            this.접속PORT = 설정.접속PORT;
            
        }
    }
   
    public class 환경설정
    {
        public bool 병렬처리여부 { get; set; }
        public string 상태컴퓨터 { get; set; } = string.Empty;
        public string 처리구분 { get; set; } = string.Empty;
        public string dataTable접속 { get; set; } = string.Empty;
        public string 접속IP { get; set; } = string.Empty;
        public int 접속PORT { get; set; } 


    }

}
