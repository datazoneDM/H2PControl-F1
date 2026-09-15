H2PControl-F1 - PDF Merge, Bookmarking & Page Numbering Engine
대용량 문서 자동화 파이프라인에서 보험증권, 기본약관, 선택특약, 별표 등 세션별로 분할된 PDF 문서를 고객 맞춤형으로 병합(Merge)하고, 동적 목차 쪽번호 날인 및 계층형 북마크(Outlines) 트리를 자동 생성하는 독립 백그라운드 프로세싱 엔진입니다.

🛠 주요 기능 및 특징
PDF 고속 병합 및 문서 재구성 (PdfMerger)

보통약관(H), 특별약관(B), 제도성약관(T), 별표(S), 목차(IX) 등 분할 생성된 PDF 세션을 단일 고객 문서로 정밀 결합

우편(P), 모바일(M), 이메일(E), 로컬(L) 등 발송 채널별 규격에 맞춘 출력 분기

양면 인쇄 및 우편 발송 규격에 맞춘 짝/홀수 페이지 검증, 공백 페이지 자동 보정 삽입

증권 내 주소 페이지를 분석하여 인쇄 규격에 맞게 문서 맨 뒤로 재배치하는 기능 지원

동적 쪽번호 및 텍스트 렌더링 (ShowTextAligned)

임베디드 폰트(NanumBarunGothic, 커스텀 TTF 등)를 활용하여 결합된 전체 페이지 영역 하단 중앙에 지정 포맷(- N -)의 페이지 번호 자동 날인

우편 규격 봉투 주소창 규격에 맞춘 가변 수신자 주소, 고객명, 우편번호 텍스트 오버레이 렌더링

계층형 인터랙티브 북마크 생성 (PdfOutline)

PDF 카탈로그의 기존 아웃라인을 정리하고, 문서 구조(약관/조항/별표)를 반영한 계층적 네비게이션 트리 재구성

PdfExplicitDestination을 활용한 페이지 다이렉트 점프 네비게이션 지원

문서 보안 및 암호화 (Standard Encryption)

고객별 고유 암호(주민번호 앞자리 등) 및 관리자 암호 기반의 표준 암호화(Standard Encryption AES-128) 적용 지원

프로세스 분리 및 파일 기반 비동기 연동 (Decoupled IPC)

메인 비즈니스 소프트웨어(Easy약관Plus)와 프로세스/메모리 레벨에서 물리적으로 분리

감시 파일(.병합지시 ➔ .병합완료) 폴링 방식을 통해 결합도를 낮추고 안정적인 배치 처리 보장

⚙️ 시스템 요구사항 및 환경 (Prerequisites)
플랫폼: Microsoft Visual Studio / C# .NET Framework 4.7.2 이상

핵심 PDF 라이브러리: iText 7 Core (itext7 v7.2.0)

UI/모니터링: DevExpress WinForms (프로세스 모니터링 및 대시보드 뷰어)

📁 주요 구성 모듈
PdfManager.cs: iText 7 API를 활용한 순수 PDF 병합, 쪽번호 날인, 암호화, 북마크 생성 전담 엔진 모듈

H2PControl.cs: 프로세스 감시, 파일 폴링 기반의 병합 지시 수신, 비동기 작업 큐 및 상태 대시보드 제어부

⚖️ 라이선스 및 컴플라이언스 (License & Compliance)
본 프로젝트는 오픈소스 라이브러리인 iText 7 Core를 사용하고 있으며, 이에 따라 전체 소스 코드는 GNU Affero General Public License v3.0 (AGPL-3.0)에 의거하여 배포됩니다.

[Compliance Notice]
This project is operated as an independent background PDF processing engine. The complete corresponding source code is provided strictly to comply with the AGPL-3.0 license requirements.

(본 프로젝트는 비즈니스 시스템 내에서 분리된 독립 PDF 처리 엔진으로 운영되며, 본 소스 코드는 AGPL-3.0 라이선스의 소스코드 공개 의무를 완벽히 준수하기 위한 목적으로 투명하게 공개되었습니다.)

오픈소스 준수 및 고지사항:
카피레프트(Copyleft) 전염성 고지: 본 프로그램의 코드를 직접 수정, 복사, 결합하여 재배포하거나 네트워크(SaaS 등)를 통해 원격 서비스 형태로 제공하는 경우, 해당 파생 소프트웨어의 소스 코드 역시 AGPLv3 조건에 따라 대중에게 전면 공개해야 할 의무가 발생합니다.

상업용 라이선스 취득 안내: 기업 내부 보안 규정이나 외주 계약 등으로 인해 소스 코드 공개를 원치 않는 비공개 상업적 용도로 본 라이브러리를 활용하고자 할 경우, iText 공식 판매처(iText Group NV)를 통해 상업용 라이선스(Commercial License)를 별도로 취득하시기 바랍니다.

📄 License Notice
This product includes software developed by iText Group NV.

Library: iText® 7 Core

License: GNU Affero General Public License v3.0 (AGPLv3)

Official Website: https://itextpdf.com
