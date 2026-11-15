using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using BlogStudio.Presentation;

namespace BlogStudio.Windows.Services;

/// <summary>
/// Windows 작업 스케줄러에 "매일 HH:mm에 BlogStudio.exe --auto-generate" 작업을 등록한다.
/// 로그인한 상태에서만 실행되고(Claude Code 로그인 정보 사용), 그 시각에 PC가 꺼져 있었으면 켠 뒤 바로 실행.
/// </summary>
public sealed class WindowsDailyScheduler : IDailyScheduler
{
    public const string TaskName = @"BlogStudio\DailyPost";
    public const string AutoArgument = "--auto-generate";

    public void Apply(bool enabled, TimeOnly time)
    {
        if (!enabled)
        {
            Run("/Delete", "/TN", TaskName, "/F");   // 없으면 실패하지만 무시
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 위치를 알 수 없어요");
        var xmlPath = Path.Combine(Path.GetTempPath(), "BlogStudio-DailyPost.xml");
        File.WriteAllText(xmlPath, BuildTaskXml(exe, time, Environment.UserDomainName + "\\" + Environment.UserName),
            Encoding.Unicode);
        try
        {
            var (code, output) = Run("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
            if (code != 0) throw new InvalidOperationException(output.Trim());
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    internal static string BuildTaskXml(string exe, TimeOnly time, string user)
    {
        var start = DateTime.Today.Add(time.ToTimeSpan()).ToString("yyyy-MM-dd'T'HH:mm:ss");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Blog Studio — 매일 오늘의 글 만들기</Description></RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{start}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>{AutoArgument}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) Run(params string[] args)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, output);
    }
}
