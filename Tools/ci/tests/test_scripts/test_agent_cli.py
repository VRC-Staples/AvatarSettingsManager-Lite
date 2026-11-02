"""Behavior checks for the dependency-free agent CLI. No Unity or network."""
import contextlib
import importlib.machinery
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[4]
CLI = ROOT / 'bin/agent-cli'
loader = importlib.machinery.SourceFileLoader('agent_cli', str(CLI))
spec = importlib.util.spec_from_loader(loader.name, loader)
cli = importlib.util.module_from_spec(spec)
loader.exec_module(cli)


class AgentCliTests(unittest.TestCase):
    def invoke(self, *args):
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            code = cli.main(list(args))
        return code, output.getvalue()

    def test_real_entrypoint_from_another_directory(self):
        with tempfile.TemporaryDirectory() as folder:
            result = subprocess.run([sys.executable, str(CLI), '--json', 'status'], cwd=folder, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        actual = json.loads(result.stdout)
        expected = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        self.assertEqual(actual['head'], expected)
        self.assertIsInstance(actual['dirty'], list)

    def test_shell_entrypoint_keeps_lf(self):
        attribute = subprocess.check_output(['git', 'check-attr', 'eol', '--', 'bin/agent-cli'], cwd=ROOT, text=True)
        self.assertEqual(attribute.strip(), 'bin/agent-cli: eol: lf')
        self.assertNotIn(b'\r\n', CLI.read_bytes())

    def test_json_errors_and_unknown_options(self):
        for argv in [('not-a-command', '--json'), ('status', '--bad-option', '--json'), ('repeats', '--limit', '0', '--json')]:
            with self.subTest(argv=argv):
                code, output = self.invoke(*argv)
                self.assertEqual(code, 2)
                self.assertIn('error', json.loads(output))

    def test_json_flag_on_either_side(self):
        left = self.invoke('--json', 'paths')
        right = self.invoke('paths', '--json')
        self.assertEqual(left, right)
        self.assertEqual(json.loads(left[1])['paths']['source']['path'], 'Packages/com.staples.asm-lite')

    def test_read_limit_match_full_and_path_boundary(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'sample.md').write_text('# One\ntext\n# Two\n', encoding='utf-8')
            with patch.object(cli, 'ROOT', root):
                code, output = self.invoke('read', 'sample.md', '--limit', '1', '--json')
                data = json.loads(output)
                self.assertEqual(code, 0)
                self.assertTrue(data['truncated'])
                self.assertEqual(data['matches'], 2)
                code, output = self.invoke('read', 'sample.md', '--match', 'text', '--json')
                self.assertEqual(json.loads(output)['lines'], [{'line': 2, 'text': 'text'}])
                code, output = self.invoke('read', 'sample.md', '--full', '--json')
                self.assertEqual(len(json.loads(output)['lines']), 3)
                self.assertEqual(self.invoke('read', '../outside.txt', '--json')[0], 2)
                self.assertEqual(self.invoke('read', 'missing.txt', '--json')[0], 2)

    def test_source_topics_read_current_files(self):
        for topic in cli.SOURCES:
            code, output = self.invoke('source', topic, '--limit', '1', '--json')
            self.assertEqual(code, 0, output)
            self.assertEqual(json.loads(output)['path'], cli.SOURCES[topic])

    def test_find_is_literal_bounded_and_skips_ignored_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            subprocess.run(['git', 'init', '--quiet', str(root)], check=True)
            (root / '.gitignore').write_text('private.cs\n', encoding='utf-8')
            (root / 'visible.cs').write_text('needle[\nneedle[\n', encoding='utf-8')
            (root / 'private.cs').write_text('needle[\n', encoding='utf-8')
            with patch.object(cli, 'ROOT', root):
                code, output = self.invoke('find', 'needle[', '--limit', '1', '--json')
                data = json.loads(output)
                self.assertEqual(code, 0)
                self.assertTrue(data['limit_reached'])
                self.assertEqual(data['hits'][0]['file'], 'visible.cs')
                code, output = self.invoke('find', 'needle[', '--json')
                self.assertEqual(len(json.loads(output)['hits']), 2)

    def test_unity_recipes_do_not_launch_processes(self):
        with patch.object(cli, 'run', side_effect=AssertionError('unexpected launch')):
            for name in cli.RECIPES:
                code, output = self.invoke(name, '--json')
                self.assertEqual(code, 0)
                self.assertEqual(json.loads(output)['execution'], 'not-run')

    def test_historical_shell_is_data_not_executable(self):
        catalog = {'items': [{'command': 'command-dangerous', 'kind': 'command', 'key': 'rm -rf /', 'count': 2, 'sessions': 2, 'refs': [], 'mode': 'recipe'}]}
        with patch.object(cli, 'load_catalog', return_value=catalog), patch.object(cli, 'run', side_effect=AssertionError('historical shell executed')):
            code, output = self.invoke('command-dangerous', '--json')
            self.assertEqual(code, 0)
            self.assertEqual(json.loads(output)['key'], 'rm -rf /')
            self.assertEqual(json.loads(output)['execution'], 'not-run')
            self.assertEqual(self.invoke('command-dangerous', '--run', '--json')[0], 2)
            code, terse = self.invoke('command-dangerous')
            self.assertEqual(code, 0)
            self.assertEqual(len(terse.splitlines()), 3)

    def test_validator_failure_retains_exit_and_diagnostics(self):
        result = {'argv': ['python', 'validator'], 'exit_code': 1, 'stdout': 'partial', 'stderr': 'ledger mismatch'}
        with patch.object(cli, 'run', return_value=result):
            code, output = self.invoke('check-ledger', '--json')
        self.assertEqual(code, 1)
        self.assertEqual(json.loads(output)['stderr'], 'ledger mismatch')

    def test_source_inventory_does_not_claim_execution(self):
        code, output = self.invoke('tests', '--limit', '1', '--json')
        data = json.loads(output)
        self.assertEqual(code, 0)
        self.assertGreater(data['total_methods'], 0)
        self.assertEqual(len(data['methods']), 1)
        self.assertEqual(data['execution'], 'not-run')

    def test_missing_audit_does_not_break_core_commands(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(cli, 'CATALOG', Path(folder) / 'missing.json'):
            self.assertEqual(self.invoke('paths', '--json')[0], 0)
            code, output = self.invoke('coverage', '--json')
            self.assertIn('absent', json.loads(output)['limitations'][0])

    def test_every_audited_item_has_help_and_one_subcommand(self):
        catalog = cli.load_catalog()
        commands = [i['command'] for i in catalog['items']]
        self.assertEqual(len(commands), len(set(commands)))
        for name in [*cli.CORE, *commands]:
            with self.subTest(command=name), self.assertRaises(SystemExit) as stopped:
                self.invoke(name, '--help')
            self.assertEqual(stopped.exception.code, 0)


if __name__ == '__main__':
    unittest.main()
