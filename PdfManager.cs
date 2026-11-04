using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Navigation;
using iText.Kernel.Utils;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace H2PControl
{
    public class PdfManager
    {
        // ==========================================================================================================
        // 04 - 04 선택약관PDF병합 : 세션별로 (H,B,T,I)를 하나로 병합하는 단계
        // ==========================================================================================================
        public static async Task<bool> 개별PDF세션결합(string 출력파일, List<string> tmp만들파일, string[] 분리, 현재상태 상태, Action<string> logAction)
        {
            int 재처리 = 0;
            bool 실행여부 = false;
            while (true)
            {
                if (재처리 >= 3)
                {
                    break;
                }
                try
                {
                    using (FileStream 약관fsRW = new FileStream(출력파일, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                    using (PdfDocument 약관DOC = new PdfDocument(new PdfWriter(약관fsRW)))
                    {
                        PdfMerger 약관Merger = new PdfMerger(약관DOC);
                        약관Merger.SetCloseSourceDocuments(false);

                        int 순서 = 0;
                        string 이전세션 = "";
                        int 짝홀페이지 = 0;
                        string 현재파일명 = "";
                        string 발송구분 = (분리[11] == "01" || 분리[11] == "1") ? "P" : (분리[11] == "02" || 분리[11] == "2") ? "M" : "E";  // P : 우편, M : 모바일, E : 이메일
                        iText.Kernel.Geom.Rectangle pagesize = null;
                        bool 처음만페이지 = false;

                        foreach (var 만들파일 in tmp만들파일)
                        {
                            순서++;
                            string[] 파일명분리 = System.IO.Path.GetFileNameWithoutExtension(만들파일).Split('_');
                            string 세션 = 파일명분리[파일명분리.Length - 2];

                            현재파일명 = new FileInfo(만들파일).Name; // 에러확인용

                            // 세션별로 짝수페이지를 설정한다.
                            // 이전세션과 현재세션이 상이 하면서 이전세션의 페이지가 홀수이면 짝수로 세팅한다.
                            // 우편일때만 해당(이메일, 모바일제외)
                            // 현재세션이 목차이면 제외한다. ( 목차는 추가 하는 개념이 아니고 H에 삽입한다.)

                            if (순서 != 1 && 세션 != 이전세션 && 발송구분 == "P" && 세션 != "IX")
                            {
                                짝홀페이지 = 약관DOC.GetNumberOfPages();
                                // 목차가 있는 고객사
                                if (짝홀페이지 % 2 == 1)
                                {
                                    약관DOC.AddNewPage((PageSize)pagesize);  //  *** 세션이 다르면 짝수페이지 설정 ***
                                }
                            }

                            using (FileStream 개별fsRW = new FileStream(만들파일, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (PdfDocument 개별DOC = new PdfDocument(new PdfReader(개별fsRW)))
                            {
                                if (처음만페이지 == false)
                                {
                                    pagesize = 개별DOC.GetPage(1).GetPageSizeWithRotation();
                                    처음만페이지 = true;
                                }

                                int 페이지 = 개별DOC.GetNumberOfPages();

                                // 목차를 H에 삽입 하는 과정
                                // 목차는 Merge가  아니고 Insert을 해야 한다.
                                if (세션 == "IX")
                                {
                                    int 삽입페이지 = Convert.ToInt32(파일명분리[파일명분리.Length - 1]);

                                    // 일단 세션을 맞추기 위해 짝수 페이지를 맞쳐준다.
                                    // 목차가 있는 고객사.
                                    if (발송구분 == "P" && 삽입페이지 % 2 == 0)
                                    {
                                        약관DOC.AddNewPage(삽입페이지, (PageSize)pagesize);
                                        삽입페이지 += 1;
                                    }

                                    개별DOC.CopyPagesTo(1, 페이지, 약관DOC, 삽입페이지, new iText.Forms.PdfPageFormCopier());

                                    // 보통약관이 시작하는 부분으로 홀수페이지 부터 시작 할 수 있도록 목차까지 삽입한 H부분을 짝수로 만들어 준다.
                                    if (발송구분 == "P")
                                    {
                                        짝홀페이지 = 개별DOC.GetNumberOfPages();
                                        int 현재까지페이지 = 삽입페이지 + 짝홀페이지 - 1;

                                        if (짝홀페이지 % 2 == 1)
                                        {
                                            // 빈페이지 삽입 (마지막 페이지)
                                            약관DOC.AddNewPage(현재까지페이지 + 1, (PageSize)pagesize);
                                        }
                                    }
                                }
                                else
                                {
                                    약관Merger.Merge(개별DOC, 1, 페이지);
                                }
                            }

                            이전세션 = 세션;
                        }

                        if (상태.고객사 == "CCK")
                        {
                            PdfFont 나눔바른고딕 = PdfFontFactory.CreateFont(@"c:\Easy약관Plus\program\Font\NanumBarunGothic.ttf", PdfEncodings.IDENTITY_H);
                            Document docw = new Document(약관DOC);
                            int 전체페이지수 = 약관DOC.GetNumberOfPages();

                            for (int i = 1; i <= 전체페이지수; i++)
                            {
                                // 현재 페이지의 크기를 가져와 가로 중앙 위치(x) 설정
                                float x = 약관DOC.GetPage(i).GetPageSize().GetWidth() / 2;
                                float y = MMtoPoint(3.0); // 하단 여백 높이 

                                // "- 1 -" 형식의 쪽번호 텍스트 생성
                                string 쪽번호 = string.Format("- {0} -", i);

                                // 지정한 위치(x, y)에 쪽번호 삽입
                                docw.ShowTextAligned(
                                    new Paragraph(쪽번호).SetFont(나눔바른고딕).SetFontSize(8).SetFontColor(iText.Kernel.Colors.ColorConstants.BLACK),
                                    x, y, i, TextAlignment.CENTER, VerticalAlignment.BOTTOM, 0);
                            }
                        }
                        실행여부 = true;
                        약관Merger.Close();

                        //개별선택약관파일(H,B,IX,T)를 병합하고 '@완료' 폴더로 이동한다.
                        string 완료폴더 = string.Format("{0}@완료\\", 상태.출력폴더);
                        foreach (var 만들파일 in tmp만들파일)
                        {
                            try
                            {
                                new FileInfo(만들파일).MoveTo(string.Format("{0}{1}", 완료폴더, new FileInfo(만들파일).Name));
                            }
                            catch (Exception)
                            { }
                        }
                        break;
                    }
                }
                catch (Exception ee)
                {
                    string 로그내용 = string.Format("{0:yy.MM.dd HH:mm:ss}[개별PDF세션결합 Er01] {1}==>{2}", DateTime.Now, ee.Message, ee.StackTrace);
                    logAction?.Invoke(로그내용);
                    재처리++;
                    await Task.Delay(3000);
                    continue;
                }
            }

            return 실행여부;
        }

        // ==========================================================================================================
        // 06 - 01 증권 + 선택약관 합치기 (P/M/E 분리 및 최종 병합)
        // ==========================================================================================================
        public static async Task<(string 메세지, int[,] 집계)> 개발증권약관병합_PME분리(dynamic 레코드, string 증권파일, string 약관파일, 현재상태 상태, Dictionary<string, string> 설정값)
        {
            string 실행여부 = "성공";
            int[,] 집계 = new int[12, 10];

            int 결합cnt = 0;
            int 증권만cnt = 0;
            int 약관만cnt = 0;
            int 개별증권Cnt = 0;

            try
            {
                string tmp서식구분 = "";
                switch ((string)레코드.서식구분)
                {
                    case "01":
                    case "1":
                        tmp서식구분 = "증권약관"; break;
                    case "02":
                    case "2":
                        tmp서식구분 = "약관만"; break;
                    case "03":
                    case "3":
                        tmp서식구분 = "증권만"; break;
                    default:
                        throw new Exception($"[오류] 서식구분 : {레코드.서식구분}");
                }

                string tmp발송구분 = "";
                switch ((string)레코드.발송구분)
                {
                    case "01":
                    case "1":
                        tmp발송구분 = "P"; break;
                    case "02":
                    case "2":
                        tmp발송구분 = "M"; break;
                    case "03":
                    case "3":
                        tmp발송구분 = "E"; break;
                    default:
                        tmp발송구분 = "L"; break;
                }

                string userPass = "";
                string owerPass = "";
                bool 패스워드여부 = false;
                string 패스워드 = 레코드.Password;
                WriterProperties setPassword = null;
                bool 북마크여부 = false;
                string 추가경로 = "";
                int 증권PageCnt = 0;
                int 약관PageCnt = 0;

                if (!string.IsNullOrWhiteSpace(패스워드) && 패스워드여부)
                {
                    userPass = 패스워드;
                    if (설정값.TryGetValue("PDFOWERPASSWORD", out string pass))
                        owerPass = pass;
                    else
                        owerPass = "";

                    setPW(out setPassword, userPass, owerPass);
                }
                else
                {
                    setPassword = null;
                }

                if (tmp발송구분 == "P" || tmp서식구분 == "증권만" || 레코드.고객사 == "CH")
                {
                    추가경로 = tmp발송구분;
                }
                else
                {
                    북마크여부 = true;
                    추가경로 = "bookmarks";
                }

                string 출력파일 = string.Format("{0}@맞춤증권\\{1}\\{2}{3}.pdf", 상태.출력폴더, 추가경로, tmp발송구분, 레코드.파일명);
                using (var fsw = new FileStream(출력파일, FileMode.Create, FileAccess.Write, FileShare.Write, 4096, false))
                {
                    if (tmp서식구분 == "증권만")
                    {
                        using (var 증권r = new FileStream(증권파일, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, false))
                        using (var 개별증권doc = new PdfDocument(new PdfReader(증권r), new PdfWriter(fsw)))
                        {
                            증권만cnt = 개별증권doc.GetNumberOfPages();
                            if (증권만cnt % 2 == 1 && tmp발송구분 == "P")
                            {
                                var pageSize = 개별증권doc.GetPage(1).GetPageSizeWithRotation();
                                if (레코드.고객사 == "LT")
                                {
                                    개별증권doc.AddNewPage((PageSize)pageSize);
                                }
                                else
                                {
                                    개별증권doc.AddNewPage(증권만cnt, (PageSize)pageSize);
                                }
                                증권만cnt++;
                            }
                        }
                    }
                    else if (tmp서식구분 == "약관만")
                    {
                        using (var 약관r = new FileStream(약관파일, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, false))
                        using (var 개별증권doc = new PdfDocument(new PdfReader(약관r), new PdfWriter(fsw)))
                        {
                            약관만cnt = 개별증권doc.GetNumberOfPages();
                            if (tmp발송구분 == "P")
                            {
                                var pageSize = 개별증권doc.GetPage(1).GetPageSizeWithRotation();

                                if (레코드.tmp03 == "1")
                                {
                                    if (약관만cnt % 2 == 0)
                                    {
                                        개별증권doc.AddNewPage((PageSize)pageSize);
                                        약관만cnt++;
                                    }

                                    PdfPage newPage = 개별증권doc.AddNewPage((PageSize)pageSize);
                                    약관만cnt++;
                                    Document docw = new Document(개별증권doc);

                                    string 폰트경로1 = @"c:\Easy약관Plus\program\Font\KBFGDISPLAYM.TTF";
                                    string 폰트경로2 = @"c:\Easy약관Plus\program\Font\KBFGDISPLAYB.TTF";

                                    PdfFont KB폰트_M = PdfFontFactory.CreateFont(폰트경로1, PdfEncodings.IDENTITY_H);
                                    PdfFont KB폰트_B = PdfFontFactory.CreateFont(폰트경로2, PdfEncodings.IDENTITY_H);

                                    string 주소1 = "";
                                    string 주소2 = "";
                                    string 고객명 = 레코드.고객명;
                                    string 우편번호 = string.Join(" ", 레코드.우편번호.ToCharArray());

                                    if (레코드.주소.Length > 0)
                                    {
                                        Encoding enc = Encoding.GetEncoding("euc-kr");
                                        string[] 주소분리 = 레코드.주소.Split(' ');

                                        int left = 0;
                                        int right = 주소분리.Length - 1;

                                        while (left <= right)
                                        {
                                            int len1 = enc.GetByteCount(주소1);
                                            int len2 = enc.GetByteCount(주소2);

                                            if (len1 <= len2)
                                            {
                                                주소1 += 주소분리[left] + " ";
                                                left++;
                                            }
                                            else
                                            {
                                                주소2 = 주소분리[right] + " " + 주소2;
                                                right--;
                                            }
                                        }
                                    }

                                    Paragraph p주소1 = new Paragraph(주소1.Trim()).SetFont(KB폰트_M).SetFontSize(10);
                                    Paragraph p주소2 = new Paragraph(주소2.Trim()).SetFont(KB폰트_M).SetFontSize(10);
                                    Paragraph p고객명 = new Paragraph(고객명).SetFont(KB폰트_B).SetFontSize(12);
                                    Paragraph p귀하 = new Paragraph("귀하").SetFont(KB폰트_M).SetFontSize(10);
                                    Paragraph p우편번호 = new Paragraph(우편번호).SetFont(KB폰트_M).SetFontSize(11);

                                    double xStart = 185.0;
                                    double yAlign = 297 - 85;
                                    int 위치 = 개별증권doc.GetNumberOfPages();

                                    주소출력(docw, xStart, yAlign, 위치, p주소1, TextAlignment.RIGHT);
                                    주소출력(docw, xStart, yAlign - 4.0, 위치, p주소2, TextAlignment.RIGHT);
                                    주소출력(docw, xStart, yAlign - 11.0, 위치, p귀하, TextAlignment.RIGHT);
                                    주소출력(docw, xStart - 8, yAlign - 11.0, 위치, p고객명, TextAlignment.RIGHT);
                                    주소출력(docw, xStart, yAlign - 18.0, 위치, p우편번호, TextAlignment.RIGHT);
                                    docw.Close();
                                }
                                else
                                {
                                    if (약관만cnt % 2 != 0)
                                    {
                                        개별증권doc.AddNewPage((PageSize)pageSize);
                                        약관만cnt++;
                                    }
                                }
                            }
                        }
                    }
                    else if (tmp서식구분 == "증권약관")
                    {
                        using (var 증권Stream = new FileStream(증권파일, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, false))
                        using (var 약관Stream = new FileStream(약관파일, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, false))
                        using (var 증권Doc = new PdfDocument(new PdfReader(증권Stream)))
                        using (var 약관Doc = new PdfDocument(new PdfReader(약관Stream)))
                        using (var 개별증권doc = new PdfDocument(new PdfWriter(fsw)))
                        using (var docw = new Document(개별증권doc))
                        {
                            PdfMerger merger = new PdfMerger(개별증권doc).SetCloseSourceDocuments(false);

                            증권PageCnt = 증권Doc.GetNumberOfPages();
                            약관PageCnt = 약관Doc.GetNumberOfPages();

                            if (레코드.고객사 == "LT")
                            {
                                merger.Merge(증권Doc, 1, 증권PageCnt);
                            }
                            else
                            {
                                merger.Merge(증권Doc, 1, 증권PageCnt - 1);
                            }

                            Rectangle pagesize = 개별증권doc.GetPage(1).GetPageSizeWithRotation();
                            if (개별증권doc.GetNumberOfPages() % 2 != 0 && tmp발송구분 == "P")
                            {
                                개별증권doc.AddNewPage((PageSize)pagesize);
                            }

                            merger.Merge(약관Doc, 1, 약관PageCnt);

                            // 직접 호출 (UI 의존성인 실행() 래퍼 제거)
                            string 쪽번호결과 = await 목차쪽번호출력(docw, 개별증권doc, 약관PageCnt);
                            if (쪽번호결과 == "실패")
                            {
                                throw new Exception("목차쪽번호출력 오류");
                            }

                            int 개별증권pagecnt = 개별증권doc.GetNumberOfPages();
                            if (레코드.고객사 == "LT")
                            {
                                if (개별증권pagecnt % 2 != 0 && tmp발송구분 == "P")
                                {
                                    개별증권doc.AddNewPage((PageSize)pagesize);
                                }
                            }
                            else
                            {
                                if (개별증권pagecnt % 2 == 0 && tmp발송구분 == "P")
                                {
                                    개별증권doc.AddNewPage((PageSize)pagesize);
                                }
                                merger.Merge(증권Doc, 증권PageCnt, 증권PageCnt);
                            }

                            개별증권Cnt = 개별증권doc.GetNumberOfPages();
                        }
                    }
                }

                int 행 = Convert.ToInt32(레코드.발송구분) * 2 - 1;
                int 열 = Convert.ToInt32(레코드.서식구분);
                int 장수 = 0;

                if (tmp서식구분 == "증권만" || tmp서식구분 == "약관만")
                {
                    집계[행, 0]++;
                    집계[행, 열]++;
                    집계[행 + 1, 0] += (int)Math.Ceiling((double)증권만cnt / 2.0) + (int)Math.Ceiling((double)약관만cnt / 2.0);

                    if (열 == 2)
                    {
                        장수 = (int)Math.Ceiling((double)약관만cnt / 2.0);
                        집계[행 + 1, 열] += 장수;
                    }
                    else
                    {
                        장수 = (int)Math.Ceiling((double)증권만cnt / 2.0);
                        집계[행 + 1, 열] += 장수;
                    }

                    집계[9, 0]++;
                    집계[9, 열]++;
                    집계[10, 열] += 장수;
                    집계[10, 0] = 집계[10, 1] + 집계[10, 2] + 집계[10, 3];
                }
                if (tmp서식구분 == "증권약관")
                {
                    집계[행 + 1, 4] += (int)Math.Ceiling((double)증권PageCnt / 2.0);
                    집계[행 + 1, 5] += (int)(Math.Ceiling((double)개별증권Cnt / 2.0)) - (int)Math.Ceiling((double)증권PageCnt / 2.0);
                    집계[행, 0]++;
                    집계[행, 열]++;

                    int 장수1 = (int)Math.Ceiling((double)개별증권Cnt / 2.0);
                    집계[행 + 1, 0] += 장수1;

                    int 장수2 = (int)Math.Ceiling((double)개별증권Cnt / 2.0);
                    집계[행 + 1, 열] += 장수2;

                    집계[9, 0]++;
                    집계[9, 열]++;
                    집계[10, 열] += 장수2;
                    집계[10, 4] += (int)Math.Ceiling((double)증권PageCnt / 2.0);
                    집계[10, 5] += (int)(Math.Ceiling((double)개별증권Cnt / 2.0)) - (int)Math.Ceiling((double)증권PageCnt / 2.0);
                    집계[10, 0] = 집계[10, 1] + 집계[10, 2] + 집계[10, 3];
                }

                if (북마크여부)
                {
                    string 북마크제목 = string.Format("♣ {0} 고객님의 {1} ♣", 레코드.고객명, (tmp서식구분 == "증권약관") ? "보험증권, 맞춤약관" : "맞춤약관");
                    string 북마크입력파일 = 출력파일;
                    string 북마크출력파일 = string.Format("{0}@맞춤증권\\{1}\\{1}{2}.pdf", 상태.출력폴더, tmp발송구분, 레코드.파일명);

                    string 북마크결과 = await 북마크생성(북마크입력파일, 북마크출력파일, 북마크제목, setPassword);
                    if (북마크결과 != "성공")
                    {
                        new FileInfo(북마크입력파일).CopyTo(북마크출력파일, true);
                        new FileInfo(북마크입력파일).Delete();
                        실행여부 = "북마크";
                        throw new Exception("북마크 생성 중 오류 발생");
                    }
                }

                결합cnt++;
            }
            catch (Exception e1)
            {
                실행여부 = e1.Message;
            }
            return (실행여부, 집계);
        }

        // ==========================================================================================================
        // 06 - 02 목차쪽번호 출력(증권+약관중 약관부분만 출력한다.
        // ==========================================================================================================
        public static async Task<string> 목차쪽번호출력(Document docw, PdfDocument 개별증권doc, int 약관PageCnt)
        {
            return await Task.Run(() =>
            {
                string 실행여부 = "성공";
                try
                {
                    PdfFont 폰트 = PdfFontFactory.CreateFont(
                        @"c:\Easy약관Plus\program\Font\NanumBarunGothic.ttf",
                        PdfEncodings.IDENTITY_H);

                    docw.SetFontSize(10).SetFont(폰트).SetFontColor(ColorConstants.BLACK);

                    int start = 개별증권doc.GetNumberOfPages() - 약관PageCnt + 1;
                    int end = 개별증권doc.GetNumberOfPages();

                    float x = MMtoPoint(105);
                    float y = MMtoPoint(5.5f);

                    int 번호 = 0;
                    for (int j = start; j <= end; j++)
                    {
                        번호++;
                        string 일련번호 = string.Format("- {0} -", 번호);
                        docw.ShowTextAligned(new Paragraph(일련번호), x, y, j,
                            TextAlignment.CENTER, VerticalAlignment.BOTTOM, 0);
                    }
                }
                catch (Exception)
                {
                    실행여부 = "실패";
                    throw;
                }
                return 실행여부;
            });
        }

        private static void 주소출력(Document docw, double X, double Y, int 위치, Paragraph 항목, TextAlignment 정렬)
        {
            docw.ShowTextAligned(항목, (float)MMtoPoint(X), (float)MMtoPoint(Y), 위치, 정렬, VerticalAlignment.BOTTOM, 0);
        }

        public static void setPW(out WriterProperties setPassword, string userPass, string ownerPass)
        {
            byte[] user = Encoding.Default.GetBytes(userPass);
            byte[] owner = Encoding.Default.GetBytes(ownerPass);
            setPassword = new WriterProperties().SetStandardEncryption(user, owner, EncryptionConstants.ALLOW_PRINTING, EncryptionConstants.ENCRYPTION_AES_128);
        }

        public static float MMtoPoint(double x)
        {
            float tmpx;
            tmpx = (float)(x * 2.83465);
            return tmpx;
        }

        // ==========================================================================================================
        // 06 - 03 목차Bookmarks 생성 모듈 
        // ==========================================================================================================
        public static async Task<string> 북마크생성(string 입력, string 출력, string 북마크제목, WriterProperties setPassword)
        {
            string 실행여부 = "성공";
            try
            {
                List<string> 편집 = new List<string>();
                using (PdfDocument pdfDoc = new PdfDocument(new PdfReader(입력)))
                {
                    PdfNameTree destsTree = pdfDoc.GetCatalog().GetNameTree(PdfName.Dests);
                    PdfOutline outlines = pdfDoc.GetOutlines(false);
                    if (outlines == null)
                    {
                        return "실패";
                    }
                    IList<PdfOutline> bookmarks = outlines.GetAllChildren();
                    int 묶음cnt = 0;

                    foreach (PdfOutline bookmark in bookmarks)
                    {
                        string 저장 = "아니요";
                        묶음cnt++;
                        bool 파일명제외 = true;

                        실행여부 = await 북마크타이틀(bookmark, destsTree.GetNames(), pdfDoc, 0, 편집, 저장, 묶음cnt, 파일명제외);
                        if (실행여부 != "성공")
                            return 실행여부;
                    }
                }

                using (PdfDocument pdfDoc = (setPassword != null)
                    ? new PdfDocument(new PdfReader(입력), new PdfWriter(출력, setPassword))
                    : new PdfDocument(new PdfReader(입력), new PdfWriter(출력)))
                {
                    실행여부 = await 북마크상세출력(pdfDoc, 편집, 북마크제목);
                    if (실행여부 != "성공")
                        return 실행여부;
                }
            }
            catch (Exception)
            {
                실행여부 = "실패";
            }
            return 실행여부;
        }

        private static async Task<string> 북마크타이틀(PdfOutline outline, IDictionary<string, PdfObject> result, PdfDocument pdfDoc,
                                    int stepCnt, List<string> 편집, string 저장, int 묶음cnt, bool 파일명제외)
        {
            string 실행여부 = "성공";
            try
            {
                if (outline != null)
                {
                    string bookmarkTitle = "";
                    int 페이지 = -1;
                    bool 목차페이지없음 = false;

                    try
                    {
                        bookmarkTitle = outline.GetTitle();
                        페이지 = pdfDoc.GetPageNumber((PdfDictionary)outline.GetDestination().GetDestinationPage(result));
                    }
                    catch (Exception)
                    {
                        목차페이지없음 = true;
                    }

                    if (파일명제외 == true ||
                        bookmarkTitle.Trim().Replace(" ", "") == "(내용없음)" ||
                        bookmarkTitle.Trim().Replace(" ", "") == "개요" ||
                        목차페이지없음 == true)
                    {
                        저장 = "아니요";
                    }

                    파일명제외 = false;
                    if (저장 == "예")
                    {
                        stepCnt++;
                        if (bookmarkTitle.Trim().Replace(" ", "") == "목차")
                        {
                            for (int i = 0; i < 편집.Count - 1; i++)
                            {
                                int S = Convert.ToInt32(편집[i].Split('|')[3].Trim());
                                int E = Convert.ToInt32(편집[i + 1].Split('|')[3].Trim());

                                if (페이지 > S && 페이지 < E)
                                {
                                    편집.Insert(i + 1, string.Format("{0:000}|{1:000}|{2}|{3}", 묶음cnt, stepCnt, bookmarkTitle.Trim(), 페이지));
                                    break;
                                }
                            }
                        }
                        else
                        {
                            편집.Add(string.Format("{0:000}|{1:000}|{2}|{3}", 묶음cnt, stepCnt, bookmarkTitle.Trim(), 페이지));
                        }
                    }

                    if (bookmarkTitle.Trim().Replace(" ", "") == "개요" ||
                        bookmarkTitle.Trim().Replace(" ", "") == "(내용없음)" ||
                        목차페이지없음 == true)
                    {
                        저장 = "예";
                    }

                    IList<PdfOutline> kids = outline.GetAllChildren();
                    if (kids != null)
                    {
                        foreach (PdfOutline kid in kids)
                        {
                            실행여부 = await 북마크타이틀(kid, result, pdfDoc, stepCnt, 편집, 저장, 묶음cnt, 파일명제외);
                            if (실행여부 != "성공")
                                return 실행여부;
                        }
                    }
                }
            }
            catch (Exception)
            {
                실행여부 = "실패";
                throw;
            }
            return 실행여부;
        }

        private static async Task<string> 북마크상세출력(PdfDocument pdfDoc, List<string> 편집, string 북마크제목)
        {
            string 실행여부 = "성공";
            try
            {
                // 기존 북마크(Outline) 제거의 올바른 iText7 API 방식
                pdfDoc.GetCatalog().GetPdfObject().Remove(PdfName.Outlines);

                PdfOutline rootOutline = pdfDoc.GetOutlines(true);
                PdfOutline 기본목차 = rootOutline.AddOutline(북마크제목);
                기본목차.AddDestination(PdfExplicitDestination.CreateFit(pdfDoc.GetPage(1)));

                List<string> 세션묶음 = new List<string>();
                string 비교묶음 = 편집[0].Substring(0, 3);
                string 현재묶음 = 편집[0].Substring(0, 3);

                for (int i = 0; i < 편집.Count; i++)
                {
                    현재묶음 = 편집[i].Substring(0, 3);
                    if (현재묶음 != 비교묶음)
                    {
                        실행여부 = await 하위북마크생성(기본목차, 세션묶음, pdfDoc);
                        if (실행여부 != "성공") return 실행여부;

                        세션묶음 = new List<string>();
                        비교묶음 = 현재묶음;
                    }

                    세션묶음.Add(편집[i]);

                    if (i == 편집.Count - 1)
                    {
                        실행여부 = await 하위북마크생성(기본목차, 세션묶음, pdfDoc);
                        if (실행여부 != "성공") return 실행여부;
                    }
                }
            }
            catch (Exception)
            {
                실행여부 = "실패";
            }
            return 실행여부;
        }

        private static async Task<string> 하위북마크생성(PdfOutline 기본목차, List<string> 세션묶음, PdfDocument pdfDoc)
        {
            string 실행여부 = "성공";
            try
            {
                List<PdfOutline> lst목차 = new List<PdfOutline>();
                lst목차.Add(기본목차);

                PdfOutline tmp목차;
                int 기준차수 = 0;
                int 현재차수 = 0;
                int 차이차수 = -1;

                for (int i = 0; i < 세션묶음.Count; i++)
                {
                    string[] 분리 = 세션묶음[i].Split('|').Select(P => P.Trim()).ToArray();
                    if (i == 0) { 차이차수 = Convert.ToInt32(분리[1]) - 1; }

                    int tmp현재차수 = Convert.ToInt32(분리[1]) - 차이차수;
                    현재차수 = tmp현재차수 <= 0 ? 1 : tmp현재차수;
                    tmp목차 = lst목차[현재차수 - 1].AddOutline(분리[2]);
                    tmp목차.AddDestination(PdfExplicitDestination.CreateFit(pdfDoc.GetPage(Convert.ToInt32(분리[3]))));

                    if (기준차수 < 현재차수)
                    {
                        lst목차.Add(tmp목차);
                        기준차수 = 현재차수;
                    }
                    else
                    {
                        lst목차[현재차수] = tmp목차;
                    }
                }
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1));
                실행여부 = "실패";
            }
            return 실행여부;
        }
    }
}