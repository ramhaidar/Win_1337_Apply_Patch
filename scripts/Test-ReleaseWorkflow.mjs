import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const workflow = new URL('../.github/workflows/release.yml', import.meta.url);
const sha = 'a'.repeat(40);
const workflowSha = 'b'.repeat(40);
const version = 'v2.4';
const repository = 'ramhaidar/Win_1337_Apply_Patch';
const defaultBranch = 'master';
const time = '2026-10-01T06:16:59Z';
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

function body(name) {
    assert.ok(fs.existsSync(workflow), 'Manual release workflow is missing.');
    const text = fs.readFileSync(workflow, 'utf8');
    const begin = text.indexOf(`// BEGIN ${name}`);
    const end = text.indexOf(`// END ${name}`, begin);
    assert.ok(begin >= 0 && end > begin, `Missing inline block ${name}`);
    return text.slice(text.indexOf('\n', begin) + 1, text.lastIndexOf('\n', end)).split('\n').map(line => line.slice(12)).join('\n');
}

function fixture(options = {}) {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'patch-promotion-tests-'));
    const wanted = options.version ?? version;
    const names = ['framework-dependent', 'self-contained'].map(mode => `Win_1337_Patch-${wanted}-win-x64-${mode}.zip`);
    const artifacts = names.map((name, index) => {
        const bytes = Buffer.from(`fixture zip ${index}`);
        fs.writeFileSync(path.join(directory, name), bytes);
        return { name, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
    });
    fs.writeFileSync(path.join(directory, 'SHA256SUMS'), artifacts.map(asset => `${asset.sha256}  ${asset.name}\n`).join(''));
    fs.writeFileSync(path.join(directory, 'provenance.json'), JSON.stringify({
        schemaVersion: 1, repository, sourceTag: wanted, sourceCommit: sha,
        commitTimeUtc: time, sourceStatus: 'pending-tag',
        workflowRef: `${repository}/.github/workflows/release.yml@refs/heads/${defaultBranch}`,
        workflowSha, runUrl: `https://github.com/${repository}/actions/runs/123`,
        sdkVersion: '10.0.401', runtimeVersion: '10.0.12', assemblyVersion: '2.4.0.0',
        publishModes: ['framework-dependent', 'self-contained'], artifacts,
    }));
    const calls = [];
    const outputs = {};
    // The tag does not exist until this run creates it, mirroring the real promote job.
    const state = { tagCreated: false };
    const github = { rest: {
        git: {
            async getRef(args) {
                calls.push(['getRef', args]);
                const refVersion = args.ref.replace('tags/', '');
                if (options.tagExists) return { data: { ref: `refs/tags/${refVersion}`, object: { type: 'commit', sha } } };
                if (!state.tagCreated) throw Object.assign(new Error('not found'), { status: 404 });
                const object = options.annotated
                    ? { type: 'tag', sha: 'd'.repeat(40) }
                    : { type: 'commit', sha: options.createdTagSha ?? sha };
                return { data: { ref: `refs/tags/${refVersion}`, object } };
            },
            async getTag() { return { data: { object: { type: 'commit', sha: options.createdTagSha ?? sha } } }; },
            async createRef(args) {
                calls.push(['createRef', args]);
                if (options.createRefFailure) throw new Error('createRef failed');
                if (options.createRefMismatch) return { data: { ref: 'refs/tags/somewhere-else' } };
                state.tagCreated = true;
                return { data: { ref: args.ref } };
            },
        },
        repos: {
            async get() { return { data: { default_branch: options.defaultBranch ?? defaultBranch } }; },
            async getCommit() { return { data: { sha, commit: { committer: { date: time } } } }; },
            async listReleases(args) {
                if (options.apiError) throw Object.assign(new Error('API unavailable'), { status: 403 });
                if (options.existing && args.page === 1) return { data: Array.from({ length: 100 }, (_, i) => ({ tag_name: `old-${i}`, draft: false })) };
                if (options.existing && args.page === 2) return { data: [{ tag_name: wanted, draft: true }] };
                return { data: [] };
            },
            async createRelease(args) {
                calls.push(['createRelease', args]);
                assert.equal(args.draft, true);
                assert.equal(args.tag_name, wanted);
                assert.equal(args.target_commitish, sha);
                if (options.createFailure) throw new Error('create failed');
                return { data: { id: 42, draft: true, tag_name: wanted, upload_url: 'https://uploads.github.test/release/42/assets{?name,label}', html_url: 'https://github.test/draft/42' } };
            },
            async uploadReleaseAsset(args) {
                calls.push(['uploadReleaseAsset', args]);
                assert.ok(Buffer.isBuffer(args.data));
                const count = calls.filter(call => call[0] === 'uploadReleaseAsset').length;
                if (options.uploadFailure && count === 2) throw new Error('upload failed');
                return { data: { name: args.name, state: 'uploaded', size: args.data.length } };
            },
        },
    } };
    const env = {
        RELEASE_VERSION: wanted, SOURCE_SHA: sha, COMMIT_TIME: time,
        ARTIFACT_DIRECTORY: directory, GITHUB_REPOSITORY: options.GITHUB_REPOSITORY ?? repository,
        GITHUB_WORKFLOW_REF: `${repository}/.github/workflows/release.yml@refs/heads/${defaultBranch}`,
        GITHUB_WORKFLOW_SHA: workflowSha, GITHUB_RUN_ID: '123', GITHUB_SERVER_URL: 'https://github.com',
    };
    const context = {
        repo: { owner: options.owner ?? 'ramhaidar', repo: options.repoName ?? 'Win_1337_Apply_Patch' },
        ref: options.ref ?? `refs/heads/${defaultBranch}`,
        sha: options.sha ?? sha,
    };
    return {
        directory, names, calls, outputs, env, context, state,
        async run(name) {
            const fn = new AsyncFunction('github', 'context', 'core', 'require', 'process', body(name));
            return fn(github, context, { setOutput: (key, value) => { outputs[key] = value; }, info() {} }, require, { env });
        },
        count(kind) { return calls.filter(call => call[0] === kind).length; },
        dispose() { fs.rmSync(directory, { recursive: true, force: true }); },
    };
}

test('Version input with a leading v resolves against the dispatched commit without creating a tag', async () => {
    const f = fixture();
    try {
        await f.run('resolve-version');
        assert.equal(f.outputs.version, version);
        assert.equal(f.outputs.source_sha, sha);
        assert.equal(f.outputs.commit_time, time);
        assert.equal(f.count('createRef'), 0, 'Resolution must never create the tag.');
        assert.equal(f.count('createRelease'), 0, 'Resolution must never create a release.');
    } finally { f.dispose(); }
});

for (const candidate of ['2.4', 'V2.4', 'v', '../main', 'v2.4;exit', 'v2.4.lock', `v${'a'.repeat(64)}`]) {
    test(`Version without a valid leading v is rejected: ${JSON.stringify(candidate)}`, async () => {
        // Artifacts always come from a valid run; only the dispatched input is hostile here.
        const f = fixture();
        try {
            f.env.RELEASE_VERSION = candidate;
            await assert.rejects(f.run('resolve-version'));
            assert.equal(f.count('createRef'), 0);
        } finally { f.dispose(); }
    });
}

for (const options of [
    { tagExists: true }, { existing: true }, { apiError: true },
    { ref: 'refs/heads/feature' }, { GITHUB_REPOSITORY: 'fork/repository' },
]) {
    test(`Resolution precondition fails before any tag creation: ${JSON.stringify(options)}`, async () => {
        const f = fixture(options);
        try {
            await assert.rejects(f.run('resolve-version'));
            assert.equal(f.count('createRef'), 0, 'No tag may be created after a failed precondition.');
            assert.equal(f.count('createRelease'), 0);
        } finally { f.dispose(); }
    });
}

test('Promotion creates the version tag then uploads exactly the four verified assets', async () => {
    const f = fixture();
    try {
        await f.run('promote-draft');
        assert.equal(f.count('createRef'), 1, 'The version tag must be created exactly once.');
        assert.equal(f.calls.find(call => call[0] === 'createRef')[1].ref, `refs/tags/${version}`);
        assert.equal(f.calls.find(call => call[0] === 'createRef')[1].sha, sha, 'The tag must point at the built commit.');
        assert.equal(f.count('createRelease'), 1);
        assert.deepEqual(
            f.calls.filter(call => call[0] === 'uploadReleaseAsset').map(call => call[1].name),
            [...f.names, 'SHA256SUMS', 'provenance.json'],
        );
        for (const [, args] of f.calls.filter(call => call[0] === 'uploadReleaseAsset')) {
            assert.deepEqual(args.data, fs.readFileSync(path.join(f.directory, args.name)));
        }
    } finally { f.dispose(); }
});

test('Promotion peels an annotated tag created by this run', async () => {
    const f = fixture({ annotated: true });
    try {
        await f.run('promote-draft');
        assert.equal(f.state.tagCreated, true);
        assert.equal(f.count('createRelease'), 1);
    } finally { f.dispose(); }
});

for (const options of [{ tagExists: true }, { existing: true }, { apiError: true }]) {
    test(`Promotion refuses to reuse an existing tag or release: ${JSON.stringify(options)}`, async () => {
        const f = fixture(options);
        try {
            await assert.rejects(f.run('promote-draft'));
            assert.equal(f.count('createRef'), 0, 'An existing tag must never be moved or overwritten.');
            assert.equal(f.count('createRelease'), 0);
        } finally { f.dispose(); }
    });
}

for (const options of [{ createRefMismatch: true }, { createdTagSha: 'c'.repeat(40) }]) {
    test(`Promotion verifies the created tag resolves to the built commit: ${JSON.stringify(options)}`, async () => {
        const f = fixture(options);
        try {
            await assert.rejects(f.run('promote-draft'));
            assert.equal(f.count('createRef'), 1, 'The tag creation itself is not retried.');
            assert.equal(f.count('createRelease'), 0, 'No draft may be created against an unverified tag.');
        } finally { f.dispose(); }
    });
}

for (const mutation of ['hash', 'extra', 'missing', 'provenance-source', 'provenance-status', 'provenance-tag', 'provenance-workflow', 'wrong-repository', 'duplicate-manifest']) {
    test(`Artifact boundary rejects ${mutation} before creating any tag or draft`, async () => {
        const f = fixture();
        try {
            if (mutation === 'hash') fs.appendFileSync(path.join(f.directory, f.names[0]), 'changed');
            if (mutation === 'extra') fs.writeFileSync(path.join(f.directory, 'run.ps1'), 'untrusted');
            if (mutation === 'missing') fs.unlinkSync(path.join(f.directory, f.names[1]));
            if (mutation === 'wrong-repository') f.env.GITHUB_REPOSITORY = 'fork/repository';
            if (mutation === 'duplicate-manifest') fs.appendFileSync(path.join(f.directory, 'SHA256SUMS'), fs.readFileSync(path.join(f.directory, 'SHA256SUMS')));
            if (mutation.startsWith('provenance-')) {
                const p = path.join(f.directory, 'provenance.json');
                const data = JSON.parse(fs.readFileSync(p));
                data[mutation === 'provenance-source' ? 'sourceCommit' : mutation === 'provenance-status' ? 'sourceStatus' : mutation === 'provenance-tag' ? 'sourceTag' : 'workflowSha'] = 'c'.repeat(40);
                fs.writeFileSync(p, JSON.stringify(data));
            }
            await assert.rejects(f.run('promote-draft'));
            assert.equal(f.count('createRef'), 0, 'Invalid artifacts must not create a tag.');
            assert.equal(f.count('createRelease'), 0);
        } finally { f.dispose(); }
    });
}

for (const options of [{ createRefFailure: true }, { createFailure: true }, { uploadFailure: true }]) {
    test(`API mutation failure is not retried or cleaned up destructively: ${JSON.stringify(options)}`, async () => {
        const f = fixture(options);
        try {
            await assert.rejects(f.run('promote-draft'));
            assert.equal(f.count('createRef'), 1, 'Tag creation is attempted once.');
            assert.equal(f.count('createRelease'), options.createRefFailure ? 0 : 1);
            assert.equal(f.count('uploadReleaseAsset'), options.uploadFailure ? 2 : 0);
        } finally { f.dispose(); }
    });
}
