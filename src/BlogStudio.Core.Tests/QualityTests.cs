using BlogStudio.Core.Ai;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Quality;

namespace BlogStudio.Core.Tests;

public class QualityTests
{
    private const string Md = """
        ## 예제
        ```python
        print(sum([1, 2, 3]))
        ```
        글
        ```python
        print(1 / 0)
        ```
        ```csharp
        Console.WriteLine(1);
        ```
        ```py
        import os
        os.remove("x")
        ```
        ```python
        n = int(input())
        ```
        """;

    /// <summary>파이썬 버전 확인은 성공, 코드는 내용에 따라 성공/실패하는 가짜 실행기.</summary>
    internal sealed class FakePython : IProcessRunner
    {
        public bool Installed = true;
        public List<ProcessSpec> Runs = new();
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            if (spec.Arguments.Contains("--version"))
                return Task.FromResult(Installed ? new ProcessResult(0, "Python 3.12.1", "") : new ProcessResult(9009, "", "not found"));
            Runs.Add(spec);
            var code = File.ReadAllText(spec.Arguments[^1]);
            return Task.FromResult(code.Contains("1 / 0")
                ? new ProcessResult(1, "", "Traceback (most recent call last):\n  File \"snippet.py\", line 1, in <module>\nZeroDivisionError: division by zero\n")
                : new ProcessResult(0, "6\n", ""));
        }
    }

    [Fact]
    public void Finds_only_python_blocks_and_skips_risky_or_interactive_code()
    {
        var blocks = CodeChecker.PythonBlocks(Md);
        Assert.Equal(4, blocks.Count);
        Assert.Null(CodeChecker.SkipReason(blocks[0]));
        Assert.Contains("파일", CodeChecker.SkipReason(blocks[2]));
        Assert.Contains("입력", CodeChecker.SkipReason(blocks[3]));
        Assert.NotNull(CodeChecker.SkipReason("open('a.txt').read()"));
        Assert.NotNull(CodeChecker.SkipReason("exec('print(1)')"));
    }

    [Fact]
    public async Task Runs_safe_blocks_and_reports_errors_with_line()
    {
        var runner = new FakePython();
        var results = (await new CodeChecker(runner).CheckAsync(Md))!;

        Assert.Equal(new[] { CodeStatus.Ok, CodeStatus.Error, CodeStatus.Skipped, CodeStatus.Skipped }, results.Select(r => r.Status));
        Assert.Equal("코드 2 오류: ZeroDivisionError: division by zero (1번째 줄)", results[1].Label);
        Assert.Equal(2, runner.Runs.Count);                                     // 위험·입력 코드는 실행 안 함
        Assert.All(runner.Runs, r => Assert.Contains("-I", r.Arguments));
        Assert.Equal(CodeChecker.Timeout, runner.Runs[0].Timeout);
    }

    [Fact]
    public async Task Without_python_returns_null_and_without_code_returns_empty()
    {
        Assert.Null(await new CodeChecker(new FakePython { Installed = false }).CheckAsync(Md));
        Assert.Empty((await new CodeChecker(new FakePython()).CheckAsync("## 코드 없음"))!);
    }

    [Fact]
    public async Task Real_python_when_available()
    {
        var results = await new CodeChecker(new SystemProcessRunner()).CheckAsync(
            "```python\nprint('ok')\n```\n```python\nraise ValueError('bad')\n```");
        if (results is null) return;                                              // 파이썬이 없는 환경
        Assert.Equal(CodeStatus.Ok, results[0].Status);
        Assert.Equal(CodeStatus.Error, results[1].Status);
        Assert.Contains("ValueError: bad", results[1].Message);
    }

    [Fact]
    public void Title_ideas_are_cleaned()
    {
        var ideas = PromptBuilder.ParseTitleIdeas("1. \"파이썬 코테 문법 정리\"\n2) BFS 최단 거리\n\n- " + new string('가', 41) + "\n• BFS 최단 거리\n다섯\n여섯\n일곱");
        Assert.Equal(new[] { "파이썬 코테 문법 정리", "BFS 최단 거리", "다섯", "여섯", "일곱" }, ideas);
    }

    [Fact]
    public void Style_sample_goes_to_system_prompt_and_is_capped()
    {
        var repo = Fixtures.Themes();
        var tech = repo.Load("tech");
        var p = new PromptBuilder(repo.LoadBasePrompt()).Build(tech, tech.Curriculum["cs"], "주제", [], "",
            styleSample: "  안녕하세요 승기입니다~ " + new string('가', 3000));
        Assert.Contains("[내 문체 예시", p.System);
        Assert.Contains("안녕하세요 승기입니다~", p.System);
        Assert.DoesNotContain(new string('가', PromptBuilder.StyleSampleLimit), p.System);
        Assert.DoesNotContain("[내 문체 예시", new PromptBuilder(repo.LoadBasePrompt()).Build(tech, tech.Curriculum["cs"], "주제", [], "").System);
    }
}
