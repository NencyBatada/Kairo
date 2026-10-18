const generationRoots = [
  'agents/src/', 'agents/evaluations/', 'agents/tests/test_evaluation',
  '.github/scripts/ai-quality', '.github/workflows/ai-quality',
  'core/src/Modules/Kairo.Modules.CampaignRooms/Checks/',
  'core/src/Modules/Kairo.Modules.CampaignRooms/Create/',
  'core/src/Modules/Kairo.Modules.CampaignRooms/Plan/',
  'core/src/Modules/Kairo.Modules.Ask/',
  'core/src/Kairo.Contracts/', 'deploy/Kairo.Deploy/Evaluations.ps1',
];
const dependencyFiles = new Set([
  'agents/pyproject.toml', 'agents/uv.lock', 'agents/Dockerfile',
  'core/Directory.Packages.props', '.github/workflows/ci.yml',
  'deploy/setup-evaluations.ps1',
]);

function requiresLive(files, total) {
  if (!Array.isArray(files) || files.length !== total || total > 3000) {
    throw new Error('Incomplete changed-file inventory');
  }
  return files.some(file => [file.filename, file.previous_filename].filter(Boolean).some(
    name => dependencyFiles.has(name) || generationRoots.some(root => name.startsWith(root))));
}

function target(pr, repository, reviewedCommit) {
  if (!pr || pr.state !== 'open' || !['develop', 'main'].includes(pr.base.ref)
      || pr.base.repo.full_name !== repository || !/^[a-f0-9]{40}$/.test(pr.head.sha)) {
    throw new Error('An open pull request against develop or main is required');
  }
  if (reviewedCommit !== undefined && reviewedCommit !== pr.head.sha) {
    throw new Error('Reviewed repository branch must match the current pull-request head');
  }
  return {
    sha: pr.head.sha,
    trusted: reviewedCommit === pr.head.sha || pr.head.repo?.full_name === repository,
  };
}

module.exports = { requiresLive, target };
