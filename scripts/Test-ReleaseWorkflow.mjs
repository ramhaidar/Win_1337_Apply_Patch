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
const tag = 'v2.4';
const repository = 'ramhaidar/Win_1337_Apply_Patch';
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
    const names = ['framework-dependent', 'self-contained'].map(mode => `Win_1337_Patch-${tag}-win-x64-${mode}.zip`);
    const artifacts = names.map((name, index) => {
        const bytes = Buffer.from(`fixture zip ${index}`);
        fs.writeFileSync(path.join(directory, name), bytes);
        return { name, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
    });
    fs.writeFileSync(path.join(directory, 'SHA256SUMS'), artifacts.map(asset => `${asset.sha256}  ${asset.name}\n`).join(''));
    fs.writeFileSync(path.join(directory, 'provenance.json'), JSON.stringify({
        schemaVersion: 1, repository, sourceTag: tag, sourceCommit: sha,
        commitTimeUtc: time, sourceStatus: 'tag-checkout',
        workflowRef: `${repository}/.github/workflows/release.yml@refs/heads/main`,
        workflowSha, runUrl: `https://github.com/${repository}/actions/runs/123`,
        sdkVersion: '10.0.401', runtimeVersion: '10.0.12', assemblyVersion: '2.3.0.0',
        publishModes: ['framework-dependent', 'self-contained'], artifacts,
    }));
    const calls = [];
    const outputs = {};
    const github = { rest: {
        git: {
            async getRef(args) {
                calls.push(['getRef', args]);
                assert.equal(args.ref, `tags/${options.tag ?? tag}`);
                if (options.missing) throw Object.assign(new Error('not found'), { status: 404 });
                return { data: { ref: `refs/tags/${options.tag ?? tag}`, object: { type: options.annotated ? 'tag' : 'commit', sha: options.moved ? 'c'.repeat(40) : sha } } };
            },
            async getTag() {
                if (options.cycle) return { data: { object: { type: 'tag', sha } } };
                return { data: { object: { type: options.noncommit ? 'tree' : 'commit', sha } } };
            },
        },
        repos: {
            async getCommit() { return { data: { sha, commit: { committer: { date: time } } } }; },
            async listReleases(args) {
                if (options.apiError) throw Object.assign(new Error('API unavailable'), { status: 403 });
                if (options.existing && args.page === 1) return { data: Array.from({ length: 100 }, (_, i) => ({ tag_name: `old-${i}`, draft: false })) };
                if (options.existing && args.page === 2) return { data: [{ tag_name: tag, draft: true }] };
                return { data: [] };
            },
            async createRelease(args) {
                calls.push(['createRelease', args]);
                assert.equal(args.draft, true);
                assert.equal(args.tag_name, tag);
                assert.equal(args.target_commitish, sha);
                if (options.createFailure) throw new Error('create failed');
                return { data: { id: 42, draft: true, tag_name: tag, upload_url: 'https://uploads.github.test/release/42/assets{?name,label}', html_url: 'https://github.test/draft/42' } };
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
        RELEASE_TAG: options.tag ?? tag, SOURCE_SHA: sha, COMMIT_TIME: time,
        ARTIFACT_DIRECTORY: directory, GITHUB_REPOSITORY: repository,
        GITHUB_WORKFLOW_REF: `${repository}/.github/workflows/release.yml@refs/heads/main`,
        GITHUB_WORKFLOW_SHA: workflowSha, GITHUB_RUN_ID: '123', GITHUB_SERVER_URL: 'https://github.com',
    };
    return {
        directory, names, calls, outputs, env,
        async run(name) {
            const fn = new AsyncFunction('github', 'context', 'core', 'require', 'process', body(name));
            return fn(github, { repo: { owner: 'ramhaidar', repo: 'Win_1337_Apply_Patch' } }, { setOutput: (key, value) => { outputs[key] = value; }, info() {} }, require, { env });
        },
        dispose() { fs.rmSync(directory, { recursive: true, force: true }); },
    };
}

for (const annotated of [false, true]) {
    test(`Exact ${annotated ? 'annotated' : 'lightweight'} tag resolves to immutable SHA`, async () => {
        const f = fixture({ annotated });
        try {
            await f.run('resolve-tag');
            assert.equal(f.outputs.source_sha, sha);
            assert.equal(f.outputs.tag, tag);
            assert.equal(f.outputs.commit_time, time);
            assert.equal(f.calls.filter(call => call[0] === 'createRelease').length, 0);
        } finally { f.dispose(); }
    });
}
for (const options of [{ missing: true }, { tag: '../main' }, { tag: 'v2.4;exit' }, { annotated: true, cycle: true }, { annotated: true, noncommit: true }]) {
    test(`Invalid tag identity fails: ${JSON.stringify(options)}`, async () => {
        const f = fixture(options);
        try { await assert.rejects(f.run('resolve-tag')); } finally { f.dispose(); }
    });
}
test('Draft uploads exactly the verified four assets without rebuilding', async () => {
    const f = fixture();
    try {
        await f.run('promote-draft');
        assert.equal(f.calls.filter(call => call[0] === 'createRelease').length, 1);
        assert.deepEqual(f.calls.filter(call => call[0] === 'uploadReleaseAsset').map(call => call[1].name), [...f.names, 'SHA256SUMS', 'provenance.json']);
        for (const [, args] of f.calls.filter(call => call[0] === 'uploadReleaseAsset')) {
            assert.deepEqual(args.data, fs.readFileSync(path.join(f.directory, args.name)));
        }
    } finally { f.dispose(); }
});
for (const options of [{ moved: true }, { missing: true }, { existing: true }, { apiError: true }]) {
    test(`Remote precondition fails before draft mutation: ${JSON.stringify(options)}`, async () => {
        const f = fixture(options);
        try {
            await assert.rejects(f.run('promote-draft'));
            assert.equal(f.calls.filter(call => call[0] === 'createRelease').length, 0);
        } finally { f.dispose(); }
    });
}
for (const mutation of ['hash', 'extra', 'missing', 'provenance-source', 'provenance-workflow', 'wrong-repository', 'duplicate-manifest']) {
    test(`Artifact boundary rejects ${mutation} before creating a draft`, async () => {
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
                data[mutation === 'provenance-source' ? 'sourceCommit' : 'workflowSha'] = 'c'.repeat(40);
                fs.writeFileSync(p, JSON.stringify(data));
            }
            await assert.rejects(f.run('promote-draft'));
            assert.equal(f.calls.filter(call => call[0] === 'createRelease').length, 0);
        } finally { f.dispose(); }
    });
}
for (const options of [{ createFailure: true }, { uploadFailure: true }]) {
    test(`API mutation failure is not retried or cleaned up destructively: ${JSON.stringify(options)}`, async () => {
        const f = fixture(options);
        try {
            await assert.rejects(f.run('promote-draft'));
            assert.equal(f.calls.filter(call => call[0] === 'createRelease').length, 1);
            assert.equal(f.calls.filter(call => call[0] === 'uploadReleaseAsset').length, options.uploadFailure ? 2 : 0);
        } finally { f.dispose(); }
    });
}
