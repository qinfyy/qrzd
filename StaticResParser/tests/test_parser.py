"""静态表导出的格式、覆盖、损坏输入和输出保护测试。"""

import contextlib
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import main
from dbx import DbxPackage, MAX_ROW_SIZE, json_value, read_catalog, safe_component
from resource_names import string_id
import lz4.block
import msgpack


def make_package(directory, tables, named=False, dictionary=None):
    directory.mkdir(parents=True, exist_ok=True)
    catalog = {}
    for name, records in tables.items():
        header = {}
        body = bytearray()
        for key, value in records:
            raw = msgpack.packb(value, use_bin_type=True)
            chunk = lz4.block.compress(raw, dict=dictionary)
            header[key] = (len(body), len(chunk))
            body.extend(chunk)
        parts = {'dbx': bytes(body), 'dbxh': msgpack.packb(header, use_bin_type=True)}
        if dictionary is not None:
            parts['dbxcd'] = dictionary
        for suffix, data in parts.items():
            filename = f'{name}.{suffix}' if named else f'{string_id(f"{name}.{suffix}")}.bin'
            (directory / filename).write_bytes(data)
        catalog[name] = hashlib.md5(body).hexdigest()
    catalog_name = 'dbx_md5.json' if named else '4b1354f6.json'
    (directory / catalog_name).write_text(json.dumps(catalog), encoding='utf-8')


class ParserTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.input = self.root / 'input'
        self.output = self.root / 'output'

    def tearDown(self):
        self.temporary.cleanup()

    def run_main(self, *extra):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return main.main(['--input', str(self.input), '--output', str(self.output), *extra])

    def read_output(self, path):
        return json.loads((self.output / path).read_text(encoding='utf-8'))

    def test_known_native_hashes(self):
        pairs = {'dbx_md5.json': '4b1354f6', 'item_data.dbx': 'e8687560', 'item_data.dbxh': '15a75cf8',
                 'item_data.dbxcd': '71ca2652', 'hero.dbx': '310e68e6', 'player_exp.dbx': '5cfc781c'}
        for name, expected in pairs.items():
            self.assertEqual(string_id(name), expected)
        self.assertNotEqual(string_id('hero.dbx'), string_id('Hero.dbx'))
        with self.assertRaises(ValueError):
            string_id('a\0b')

    def test_json_types(self):
        data = {1: b'abc', '1': (2, 3), (2, 3): b'\xff'}
        converted = json_value(data)
        self.assertEqual(converted['$map'][0], [1, 'abc'])
        self.assertEqual(converted['$map'][1], ['1', {'$tuple': [2, 3]}])
        self.assertEqual(converted['$map'][2], [{'$tuple': [2, 3]}, {'$binary': 'ff'}])
        self.assertIn('$map', json_value({b'key': 1, 'key': 2}))
        self.assertIn('$map', json_value({'$tuple': [1, 2]}))
        self.assertEqual(json_value(msgpack.ExtType(2, b'abc')), {'$ext': {'code': 2, 'hex': '616263'}})
        json.dumps(json_value(float('nan')), allow_nan=False)

    def test_hashed_files_and_composite_keys(self):
        make_package(self.input, {'city_upgrade': [((1, 2), {b'name': '中央庭'.encode(), b'amount': 50})], 'empty': []})
        self.assertEqual(self.run_main(), 0)
        rows = self.read_output('dbx/city_upgrade.json')['rows']
        self.assertEqual(rows, [{'key': {'$tuple': [1, 2]}, 'value': {'name': '中央庭', 'amount': 50}}])
        self.assertEqual(self.read_output('report.json')['empty_tables'], 1)

    def test_named_files_without_manifest(self):
        make_package(self.input, {'hero': [(2, {'hp': 2486})]}, named=True)
        self.assertEqual(self.run_main(), 0)
        self.assertEqual(self.read_output('dbx/hero.json')['rows'][0]['key'], 2)

    def test_dictionary(self):
        dictionary = b'abcdefghijklmnopqrstuvwxyz' * 100
        value = {b'text': dictionary[-600:]}
        make_package(self.input, {'table': [(1, value)]}, dictionary=dictionary)
        self.assertEqual(self.run_main(), 0)
        self.assertEqual(self.read_output('dbx/table.json')['rows'][0]['value']['text'], value[b'text'].decode())
        (self.input / f'{string_id("table.dbxcd")}.bin').unlink()
        self.assertEqual(self.run_main(), 1)
        self.assertFalse((self.output / 'dbx/table.json').exists())

    def test_hotupdate_overrides_each_file_independently(self):
        base = self.input / 'apk/dbx'
        hot = self.input / 'hotupdate/dbx'
        make_package(base, {'hero': [(1, {'hp': 1})], 'exp': [(1, {'exp': 20})]})
        make_package(hot, {'hero': [(1, {'hp': 2})]})
        (hot / f'{string_id("hero.dbxh")}.bin').unlink()
        catalog = json.loads((base / '4b1354f6.json').read_bytes())
        catalog.update(json.loads((hot / '4b1354f6.json').read_bytes()))
        (hot / '4b1354f6.json').write_text(json.dumps(catalog), encoding='utf-8')
        self.assertEqual(self.run_main(), 0)
        self.assertEqual(self.read_output('dbx/hero.json')['rows'][0]['value']['hp'], 2)
        records = self.read_output('manifest.json')['tables']
        hero = next(item for item in records if item['resource'] == 'dbx/hero.dbx')
        self.assertIn('apk', hero['sources']['hero.dbxh'])
        self.assertIn('hotupdate', hero['sources']['hero.dbx'])

    def test_explicit_overlay_and_catalog_removal(self):
        hot = self.root / 'patch'
        make_package(self.input, {'hero': [(1, 1)], 'removed': [(2, 2)]})
        make_package(hot, {'hero': [(1, 9)]})
        self.assertEqual(self.run_main('--overlay', str(hot)), 0)
        self.assertEqual(self.read_output('dbx/hero.json')['rows'][0]['value'], 9)
        self.assertFalse((self.output / 'dbx/removed.json').exists())

    def test_overlay_without_its_own_catalog(self):
        dictionary = b'abcdefghijklmnopqrstuvwxyz' * 100
        make_package(self.input, {'hero': [(1, dictionary[-600:])]}, dictionary=dictionary)
        hot = self.root / 'patch'
        hot.mkdir()
        name = f'{string_id("hero.dbxcd")}.bin'
        (self.input / name).rename(hot / name)
        self.assertEqual(self.run_main('--overlay', str(hot)), 0)
        self.assertEqual(self.read_output('dbx/hero.json')['rows'][0]['value'], dictionary[-600:].decode())

    def test_packages_are_isolated(self):
        make_package(self.input / 'dbx', {'model_data': [(1, 'game')]})
        make_package(self.input / 'model_dbx', {'model_data': [(1, 'model')]})
        self.assertEqual(self.run_main(), 0)
        self.assertEqual(self.read_output('dbx/model_data.json')['rows'][0]['value'], 'game')
        self.assertEqual(self.read_output('model_dbx/model_data.json')['rows'][0]['value'], 'model')

    def test_rerun_preserves_unrelated_files(self):
        make_package(self.input, {'hero': [(1, 1)]})
        self.assertEqual(self.run_main(), 0)
        (self.output / 'notes.txt').write_text('keep', encoding='utf-8')
        self.assertEqual(self.run_main(), 0)
        self.assertEqual((self.output / 'notes.txt').read_text(), 'keep')

    def test_modified_output_is_preserved(self):
        make_package(self.input, {'hero': [(1, 1)]})
        self.assertEqual(self.run_main(), 0)
        path = self.output / 'dbx/hero.json'
        path.write_text('user edit', encoding='utf-8')
        self.assertEqual(self.run_main(), 1)
        self.assertEqual(path.read_text(), 'user edit')
        self.assertTrue((self.output / 'dbx/dbx_md5.json').is_file())

    def test_failure_does_not_leave_old_success(self):
        make_package(self.input, {'hero': [(1, 1)], 'exp': [(1, 2)]})
        self.assertEqual(self.run_main(), 0)
        (self.input / f'{string_id("hero.dbx")}.bin').write_bytes(b'corrupt')
        self.assertEqual(self.run_main(), 1)
        self.assertFalse((self.output / 'dbx/hero.json').exists())
        self.assertTrue((self.output / 'dbx/exp.json').exists())
        self.assertEqual(self.read_output('report.json')['failed_tables'], 1)
        self.assertFalse(self.read_output('report.json')['completed'])

    def test_missing_header(self):
        make_package(self.input, {'hero': [(1, 1)]})
        (self.input / f'{string_id("hero.dbxh")}.bin').unlink()
        self.assertEqual(self.run_main(), 1)
        self.assertIn('APK', self.read_output('manifest.json')['tables'][0]['error'])

    def test_corrupt_row_and_oversized_row(self):
        make_package(self.input, {'hero': [(1, 1)]})
        body_path = self.input / f'{string_id("hero.dbx")}.bin'
        body_path.write_bytes(struct.pack('<I', MAX_ROW_SIZE + 1))
        (self.input / f'{string_id("hero.dbxh")}.bin').write_bytes(msgpack.packb({1: (0, 4)}))
        (self.input / '4b1354f6.json').write_text(json.dumps({'hero': hashlib.md5(body_path.read_bytes()).hexdigest()}))
        self.assertEqual(self.run_main(), 1)
        self.assertIn('限制', self.read_output('manifest.json')['tables'][0]['error'])

    def test_bad_index(self):
        make_package(self.input, {'hero': [(1, 1)]})
        path = self.input / f'{string_id("hero.dbxh")}.bin'
        for span in ((-1, 20), (0, 999), (True, 9), 'invalid'):
            path.write_bytes(msgpack.packb({1: span}))
            self.assertEqual(self.run_main(), 1)

    def test_wrong_extension_is_not_used_for_format(self):
        make_package(self.input, {'hero': [(1, 1)]})
        path = self.input / f'{string_id("hero.dbx")}.bin'
        path.rename(path.with_suffix('.json'))
        self.assertEqual(self.run_main(), 0)

    def test_duplicate_id_and_duplicate_catalog_key(self):
        make_package(self.input, {'hero': [(1, 1)]})
        path = self.input / f'{string_id("hero.dbx")}.bin'
        path.with_suffix('.json').write_bytes(path.read_bytes())
        self.assertEqual(self.run_main(), 1)
        with self.assertRaisesRegex(ValueError, '重复键'):
            (self.input / 'bad.json').write_text('{"a":"x","a":"y"}')
            read_catalog(self.input / 'bad.json')

    def test_output_and_input_may_not_overlap(self):
        make_package(self.input, {'hero': [(1, 1)]})
        for output in (self.input, self.input / 'output', self.root):
            with self.assertRaises(ValueError):
                main.prepare_output(output, [self.input])

    def test_unmanaged_output_is_not_deleted(self):
        make_package(self.input, {'hero': [(1, 1)]})
        self.output.mkdir()
        (self.output / 'user.txt').write_text('keep')
        self.assertEqual(self.run_main(), 1)
        self.assertEqual((self.output / 'user.txt').read_text(), 'keep')

    def test_unsafe_names(self):
        for name in ('../escape', '..', 'a/b', 'a\\b', 'x:stream', 'NUL', 'CON.json', 'tail.'):
            with self.assertRaises(ValueError):
                safe_component(name)
        with self.assertRaises(ValueError):
            main.checked_target(self.output, '../outside')

    def test_only_npk_is_actionable_error(self):
        self.input.mkdir()
        (self.input / 'dbx.npk').write_bytes(b'NXPK')
        self.assertEqual(self.run_main(), 1)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
