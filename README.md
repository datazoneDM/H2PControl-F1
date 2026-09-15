# H2PControl-F1

`H2PControl-F1`은 대용량 문서 자동화 파이프라인에서 PDF 파일 결합, 목차 쪽번호 생성, 북마크(Outlines) 트리 구조 삽입 작업을 전담하여 처리하는 독립 백그라운드 프로세싱 엔진입니다.

본 프로젝트는 오픈소스 PDF 라이브러리인 **iText 7 Core (AGPLv3)**를 기반으로 동작하며, 라이선스 규정을 준수하기 위해 독립 모듈 형태로 분리되어 전체 소스 코드가 AGPLv3 조건으로 공개됩니다.

## 📌 주요 기능 (Features)

* **PDF 문서 분할 결합 (`PdfMerger`)**:
  * 가변 본문, 보통약관, 특별약관, 별표 등 세션별 분할 PDF 문서를 단일 파일로 결합
  * 우편 규격에 맞춘 짝/홀수 페이지 자동 계산 및 공백 페이지 삽입
* **목차 및 동적 쪽번호 삽입**:
  * 결합된 전체 페이지 영역을 분석하여 하단 중앙에 지정된 포맷(`- N -`)의 페이지 번호 자동 날인
* **계층형 북마크(Bookmarks / Outlines) 생성**:
  * PDF 내부 목차 텍스트 트리를 분석하여 Acrobat 북마크 네비게이션 트리 자동 구성
* **파일 기반 느슨한 결합(Decoupled IPC)**:
  * 메인 비즈니스 애플리케이션과 프로세스 레벨에서 분리되어 감시 파일(`.병합지시` ➔ `.병합완료`)을 통해 연동

## 🛠 기술 스택

* **C# / .NET Framework 4.7.2**
* **iText 7 Core (version 7.2.0, AGPLv3)**
* **DevExpress WinForms**

## ⚖️ 라이선스 고지 (License Notice)

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License (AGPLv3)**.
This module links to **iText® 7 Core**, which is licensed under the AGPLv3 by iText Group NV.
