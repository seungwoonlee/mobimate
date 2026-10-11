// 가짜 CLI 제어 환경변수(FAKECLI_*)가 프로세스 전역이고, 저장 폴더·뮤텍스도 이름으로 공유되므로 테스트는 순서대로 실행한다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
