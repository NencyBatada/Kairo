const test = require('node:test');
const assert = require('node:assert/strict');
const { requiresLive, target } = require('./ai-quality-policy.cjs');
const repository = 'NencyBatada/Kairo';
const sha = 'a'.repeat(40);
const pr = {
  state: 'open', base: { ref: 'develop', repo: { full_name: repository } },
  head: { sha, repo: { full_name: repository } },
};

test('generation, prompt, model, checking, fixtures and dependencies require live evaluation', () => {
  for (const filename of [
    'agents/src/kairo_agents/agents/plan/prompts/brief_v2.md',
    'agents/src/kairo_agents/agents/check/api.py', 'agents/evaluations/v1/cases.json',
    'agents/pyproject.toml', 'agents/uv.lock', '.github/workflows/ai-quality-live.yml',
    'core/src/Modules/Kairo.Modules.CampaignRooms/Checks/CheckRules.cs',
    'core/src/Modules/Kairo.Modules.CampaignRooms/Briefs/BriefDrafter.cs',
    'core/src/Modules/Kairo.Modules.CampaignRooms/Assets/CopyGenerator.cs',
    'core/src/Modules/Kairo.Modules.CampaignRooms/Media/ImageGenerator.cs',
    'core/src/Modules/Kairo.Modules.Accessibility/AlternativeService.cs',
  ]) assert.equal(requiresLive([{ filename }], 1), true, filename);
  assert.equal(requiresLive([{ filename: 'docs/README.md' }], 1), false);
  assert.equal(requiresLive([{ filename: 'moved.py', previous_filename: 'agents/src/old.py' }], 1), true);
});

test('missing files and API truncation fail closed', () => {
  assert.throws(() => requiresLive([], 1));
  assert.throws(() => requiresLive(new Array(3001).fill({}), 3001));
});

test('same-repository changes are trusted and forks require an identical reviewed repository ref', () => {
  assert.deepEqual(target(pr, repository), { sha, trusted: true });
  const fork = { ...pr, head: { sha, repo: { full_name: 'fork/Kairo' } } };
  assert.equal(target(fork, repository).trusted, false);
  assert.equal(target(fork, repository, sha).trusted, true);
  assert.throws(() => target(fork, repository, 'b'.repeat(40)));
  assert.throws(() => target({ ...pr, state: 'closed' }, repository));
  assert.throws(() => target({ ...pr, base: { ...pr.base, ref: 'untrusted' } }, repository));
});
