using ZL2PackBundler.Core.Analysis;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core;

public enum BaseApkKind { PatchedBuild, OfficialInjected }

public sealed record PackReport(
    BundledPackType Type,
    PackFormat Format,
    BaseApkKind BaseApkKind,
    string Name,
    string? Author,
    string? McVersion,
    string? IconSummary,
    long PackZipBytes,
    long FinalApkBytes,
    string OutputPath,
    IReadOnlyList<OfflineItem> OfflineReport,
    IReadOnlyList<GuardWarning> Warnings,
    string? CertificateInfo);
