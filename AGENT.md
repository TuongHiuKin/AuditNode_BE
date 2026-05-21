# AI Agent System Instructions & Rules

## 1. Strict TDD Contract (Test-Driven Development)
- For every new feature, API controller, core application service, backend logic, or frontend UI component that you generate, you MUST simultaneously create a corresponding Unit Test file containing active assertions.
- Tasks are considered incomplete if functional code is written without accompanying tests (xUnit/FluentAssertions for Backend, Vitest/React Testing Library for Frontend).

## 2. Automated Prompt Archiving Contract
- Monitor the conversation for user confirmation keywords such as 'Confirm', 'Lưu prompt', or 'Đã chốt'.
- Upon detecting these keywords, you must automatically extract the core successful prompt context/structure used in that turn and append it chronologically into a log file named 'PROMPT_HISTORY.md' at the project root.

## 3. Architectural Integrity Guardrails
- **Backend:** Strictly enforce .NET Clean Architecture standards. Keep clear separation between Domain, Application, Infrastructure, and API layers.
- **Frontend:** Adhere strictly to the FSD-Lite (Feature-Driven) folder structure convention. Keep components modular and single-responsibility focused.

*Note: Read and follow this file before initiating any code modification or generation task in this workspace.*