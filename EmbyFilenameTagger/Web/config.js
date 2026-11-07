define(['loading'], function (loading) {
    'use strict';

    // Emby 插件配置页的标准脚本模块。
    // HTML 与脚本分离：HTML 由 GetHtmlStream 返回，脚本由 Emby 按
    // data-controller="__plugin/filenametaggerjs" 加载本文件。

    var API_GET = 'FilenameTagger/Config';
    var API_SAVE = 'FilenameTagger/Config';
    var STYLE_ID = 'ft-style';

    /**
     * 规则种类元数据。
     *
     * 同时用数字和名字做键，因为 Emby 的 JSON 序列化会把枚举输出成名字：
     * 保存时前端发的是 0/1/2，但读回来是 "Tag"/"Genre"/"Actor"。
     * 只按数字索引会导致读回来时全部落到默认值（标签），
     * 表现为"保存后规则种类自己变了"。
     */
    var KIND_META = {
        0: { label: '标签', cls: 'ft-k-tag', hint: '命中关键词后，给条目加上这个标签' },
        1: { label: '类型', cls: 'ft-k-genre', hint: '命中关键词后，设置条目类型（会转换为 Emby 标准类型名）' },
        2: { label: '演员', cls: 'ft-k-actor', hint: '命中关键词后，把演员写入条目' },
        3: { label: '制片', cls: 'ft-k-producer', hint: '命中关键词后，把制片写入条目' },
        Tag: { label: '标签', cls: 'ft-k-tag', hint: '命中关键词后，给条目加上这个标签' },
        Genre: { label: '类型', cls: 'ft-k-genre', hint: '命中关键词后，设置条目类型（会转换为 Emby 标准类型名）' },
        Actor: { label: '演员', cls: 'ft-k-actor', hint: '命中关键词后，把演员写入条目' },
        Producer: { label: '制片', cls: 'ft-k-producer', hint: '命中关键词后，把制片写入条目' }
    };

    /** 把数字 0/1/2/3 或枚举名字统一成数字。 */
    function kindToNumber(k) {
        if (k === 0 || k === '0') return 0;
        if (k === 1 || k === '1') return 1;
        if (k === 2 || k === '2') return 2;
        if (k === 3 || k === '3') return 3;
        if (k === 'Tag') return 0;
        if (k === 'Genre') return 1;
        if (k === 'Actor') return 2;
        if (k === 'Producer') return 3;
        return 0;
    }

    /** 把数字 0/1/2/3 转成服务端能识别的枚举名。 */
    function kindToName(k) {
        var n = kindToNumber(k);
        return n === 1 ? 'Genre' : (n === 2 ? 'Actor' : (n === 3 ? 'Producer' : 'Tag'));
    }

    /**
     * 页面顶部的语法说明。
     * 集中放在顶部一次讲清，规则内部不再重复，避免每张卡片都堆一大段字。
     */
    var HELP_HTML =
        '<b>写法</b>：<code>"关键词"="目标值"</code>，多条用 <code>,</code> 分隔，引号必填。<br>' +
        '<b>多个关键词</b>：用 <code>|</code> 分隔，如 <code>"2160p"|"4K"="4K"</code>。<br>' +
        '<b>目标值留空</b>：如 <code>"周星驰"=</code>，直接写入关键词本身。<br>' +
        '<b>引号</b>：名字含 <code>=</code> <code>,</code> <code>|</code> 时靠引号区分内容与分隔符。';

    var CSS = [
        '.ft-root{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Microsoft YaHei",sans-serif;font-size:15px;color:#333;padding:8px 4px 40px 4px;max-width:1180px}',
        '.ft-root *{box-sizing:border-box}',
        '.ft-title{font-size:22px;font-weight:600;margin:0 0 8px}',
        '.ft-sub{color:#777;font-size:13px;margin-bottom:18px;line-height:1.6}',
        '.ft-card{border:1px solid #e2e2e2;border-radius:8px;background:#fff;margin-bottom:18px;overflow:hidden}',
        '.ft-card-head{padding:12px 16px;background:#f7f7f7;border-bottom:1px solid #e2e2e2;font-weight:600;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}',
        '.ft-card-body{padding:16px}',
        '.ft-switch-row{display:flex;align-items:flex-start;gap:12px;padding:10px 0;border-bottom:1px dashed #eee}',
        '.ft-switch-row:last-child{border-bottom:none}',
        '.ft-switch-row label{display:flex;align-items:center;gap:8px;cursor:pointer;font-weight:500}',
        '.ft-switch-row input[type=checkbox]{width:16px;height:16px;cursor:pointer}',
        '.ft-switch-desc{color:#888;font-size:13px;margin-top:3px;line-height:1.5}',
        '.ft-btn{border:1px solid #ccc;background:#fff;color:#333;border-radius:6px;padding:8px 16px;font-size:14px;cursor:pointer;font-family:inherit}',
        '.ft-btn:hover{background:#f0f0f0}',
        '.ft-btn.ft-primary{background:#22a06b;border-color:#22a06b;color:#fff;font-weight:500}',
        '.ft-btn.ft-primary:hover{background:#1c8b5c}',
        '.ft-btn.ft-danger{color:#c0392b;border-color:#e3b7b1}',
        '.ft-btn.ft-danger:hover{background:#fdf1f0}',
        '.ft-btn.ft-small{padding:5px 11px;font-size:13px}',
        '.ft-btn:disabled{opacity:.45;cursor:default}',
        '.ft-rule{border:1px solid #e2e2e2;border-radius:8px;margin-bottom:14px;background:#fff;overflow:hidden}',
        '.ft-rule-head{display:flex;align-items:center;gap:10px;padding:10px 14px;background:#fafafa;border-bottom:1px solid #ececec;flex-wrap:wrap}',
        '.ft-rule-head .ft-name-slot{flex:1 1 200px;min-width:160px}',
        '.ft-rule-head .ft-actions{display:flex;gap:6px;margin-left:auto}',
        '.ft-rule-body{padding:14px}',
        '.ft-rule-disabled{opacity:.55}',
        '.ft-field{margin-bottom:14px}',
        '.ft-field:last-child{margin-bottom:0}',
        '.ft-label{display:block;font-size:13px;font-weight:500;color:#555;margin-bottom:5px}',
        '.ft-hint{color:#999;font-size:12px;margin-top:4px;line-height:1.5}',
        '#ft-root input[type=text],#ft-root select{width:100%;border:1px solid #ccc;border-radius:6px;padding:8px 10px;font-size:14px;font-family:inherit;color:#333;background:#fff}',
        '#ft-root input[type=text]:focus,#ft-root select:focus{outline:none;border-color:#22a06b}',
        '#ft-root textarea.ft-mapping{width:100%;min-height:76px;border:1px solid #ccc;border-radius:6px;padding:10px 12px;font-size:14px;font-family:Consolas,"Courier New",monospace;line-height:1.7;color:#333;background:#fff;resize:vertical;white-space:pre-wrap;word-break:break-word}',
        '#ft-root textarea.ft-mapping:focus{outline:none;border-color:#22a06b}',
        '.ft-hint.ft-warn{color:#c0392b}',
        '.ft-libs{border:1px solid #ccc;border-radius:6px;padding:10px 12px;max-height:190px;overflow-y:auto;background:#fff}',
        '.ft-libs-empty{color:#999;font-size:13px}',
        '.ft-lib-item{display:flex;align-items:center;gap:8px;padding:5px 2px;cursor:pointer;font-size:14px;border-radius:4px}',
        '.ft-lib-item:hover{background:#f5f5f5}',
        '.ft-lib-item input[type=checkbox]{width:15px;height:15px;cursor:pointer;flex:0 0 auto}',
        '.ft-lib-summary{font-size:13px;color:#22a06b;margin-top:6px;font-weight:500}',
        '.ft-lib-summary.ft-none{color:#888;font-weight:400}',
        '.ft-empty{text-align:center;color:#aaa;padding:34px 12px;font-size:14px;border:1px dashed #ddd;border-radius:8px;background:#fcfcfc}',
        '.ft-loading{color:#888;padding:20px;text-align:center}',
        '.ft-count{font-size:13px;color:#888;font-weight:400}',
        // 规则种类徽标
        '.ft-kind-badge{display:inline-block;padding:6px 12px;border-radius:6px;font-size:13px;font-weight:600;white-space:nowrap;cursor:default}',
        '.ft-k-tag{background:#e8f4fd;color:#1565a8}',
        '.ft-k-genre{background:#f3ecfb;color:#6b3fa0}',
        '.ft-k-actor{background:#fdf0e6;color:#b55a12}',
        '.ft-k-producer{background:#eaf7ea;color:#2e7d32}',
        // 同时写入区域
        '.ft-also{display:flex;align-items:center;gap:16px;flex-wrap:wrap;padding:10px 12px;background:#fafafa;border:1px solid #eee;border-radius:6px;margin-bottom:14px}',
        '.ft-also-title{font-size:13px;color:#666;font-weight:500}',
        '.ft-also label{display:flex;align-items:center;gap:6px;font-size:14px;cursor:pointer}',
        '.ft-also input[type=checkbox]{width:15px;height:15px;cursor:pointer}',
        '.ft-also .ft-self{color:#999;font-size:12px}',
        // 正则开关（放在标题行右侧）
        '.ft-mapping-head{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:5px}',
        '.ft-regex-toggle{display:inline-flex;align-items:center;gap:6px;font-size:13px;color:#666;cursor:pointer;user-select:none;white-space:nowrap}',
        '.ft-regex-toggle input[type=checkbox]{width:15px;height:15px;cursor:pointer}',
        // 媒体库下拉
        '.ft-libfield{margin-bottom:10px}',
        '.ft-libfield-head{display:flex;align-items:center;gap:10px;flex-wrap:wrap}',
        '.ft-lib-badge{font-size:12px;color:#22a06b;background:#eefaf4;padding:3px 9px;border-radius:10px;max-width:60%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
        '.ft-lib-badge.ft-none{color:#999;background:#f3f3f3}',
        '.ft-lib-badge.ft-warn{color:#b55a12;background:#fdf3ea}',
        '.ft-lib-toggle{margin-left:auto;border:1px solid #ccc;background:#fff;border-radius:6px;padding:4px 12px;font-size:13px;cursor:pointer;font-family:inherit;color:#555}',
        '.ft-lib-toggle:hover{background:#f0f0f0}',
        '.ft-libfield .ft-libs{margin-top:8px;max-height:220px}',
        // 语法说明
        '.ft-help{margin-top:8px;padding:10px 12px;background:#fafafa;border:1px solid #eee;border-radius:6px;font-size:12px;line-height:1.85;color:#777}',
        '.ft-help b{color:#555;font-weight:600}',
        '.ft-help code{background:#eef1f4;color:#c7254e;padding:1px 5px;border-radius:3px;font-family:Consolas,monospace;font-size:12px}'
    ].join('\n');

    function injectStyle() {
        if (document.getElementById(STYLE_ID)) return;
        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.appendChild(document.createTextNode(CSS));
        document.head.appendChild(style);
    }

    function apiUrl(path) {
        if (window.ApiClient && typeof ApiClient.getUrl === 'function') {
            return ApiClient.getUrl(path);
        }
        return '/emby/' + path;
    }

    /**
     * Emby 的 API 要求认证：仅带 Cookie 会返回 401。
     *
     * Emby 的 ApiClient.setAuthorizationInfoIntoRequest 会把
     * X-Emby-Token 等参数附加到 URL 查询串上，因此最稳妥的方式是
     * 直接走 ApiClient.ajax（它内部会调用 fetch 并自动补 token）。
     */
    function request(path, options) {
        options = options || {};

        var api = window.ApiClient;

        if (api && typeof api.ajax === 'function' && typeof api.getUrl === 'function') {
            return api.ajax({
                type: options.method || 'GET',
                url: api.getUrl(path),
                data: options.body,
                // Emby 会把 contentType=application/json 转成 reqformat=json，
                // 服务端据此把请求体按 JSON 反序列化。
                contentType: options.body ? 'application/json' : undefined,
                dataType: 'json'
            });
        }

        // ---- 兜底：ApiClient 不可用时手动带 token ----
        var token = null;
        if (api) {
            if (typeof api.accessToken === 'function') token = api.accessToken();
            else if (api._userAuthInfo && api._userAuthInfo.AccessToken) token = api._userAuthInfo.AccessToken;
            else if (api._accessToken) token = api._accessToken;
        }

        var url = api && typeof api.getUrl === 'function' ? api.getUrl(path) : '/emby/' + path;

        // Emby 接受 api_key 查询参数作为认证方式
        if (token) {
            url += (url.indexOf('?') >= 0 ? '&' : '?') + 'api_key=' + encodeURIComponent(token);
        }

        var headers = { 'Accept': 'application/json' };
        if (options.body) headers['Content-Type'] = 'application/json';

        return fetch(url, {
            method: options.method || 'GET',
            headers: headers,
            credentials: 'same-origin',
            body: options.body
        }).then(function (r) {
            if (!r.ok) {
                throw new Error('HTTP ' + r.status + (r.status === 401 ? '（认证失败：未取得访问令牌）' : ''));
            }
            return r.json();
        });
    }

    function apiGet() {
        return request(API_GET, { method: 'GET' });
    }

    function apiSave(payload) {
        return request(API_SAVE, {
            method: 'POST',
            body: JSON.stringify(payload)
        });
    }

    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text !== undefined && text !== null) e.textContent = text;
        return e;
    }

    function toast(msg, ok) {
        var t = document.getElementById('ft-toast');
        if (!t) {
            t = el('div');
            t.id = 'ft-toast';
            t.style.cssText = 'position:fixed;right:24px;bottom:24px;padding:11px 20px;border-radius:6px;' +
                'color:#fff;font-size:14px;z-index:99999;box-shadow:0 4px 16px rgba(0,0,0,.18)';
            document.body.appendChild(t);
        }
        t.textContent = msg;
        t.style.background = ok ? '#22a06b' : '#c0392b';
        clearTimeout(t._timer);
        t._timer = setTimeout(function () { t.style.display = 'none'; }, 2600);
        t.style.display = 'block';
    }

    function newRule(kind) {
        return {
            Kind: kind,
            Kinds: [],                 // 额外要写入的种类，空 = 只写自身种类
            Enabled: true,
            Name: '',
            MappingText: '',
            Pattern: '',
            Libraries: [],
            ExcludedLibraries: []
        };
    }

    // ---------------- 渲染 ----------------

    function TaggingPage(view) {
        this.view = view;
        this.state = { config: null, libraries: [] };
    }

    TaggingPage.prototype.init = function () {
        injectStyle();
        var self = this;
        var attempts = 0;

        (function wait() {
            attempts++;
            if (window.ApiClient || attempts > 30) {
                self.load(false);
                return;
            }
            setTimeout(wait, 100);
        })();
    };

    TaggingPage.prototype.root = function () {
        return this.view.querySelector('#ft-root');
    };

    TaggingPage.prototype.render = function () {
        var root = this.root();
        if (!root) return;
        root.innerHTML = '';
        root.className = 'ft-root';

        root.appendChild(this.renderHeader());
        root.appendChild(this.renderSwitches());
        root.appendChild(this.renderRuleSection());
        root.appendChild(this.renderFooter());
    };

    TaggingPage.prototype.renderHeader = function () {
        var wrap = el('div');
        wrap.appendChild(el('h1', 'ft-title', '文件名关键词标签'));

        var sub = el('div', 'ft-sub');
        sub.innerHTML = '根据文件名中的关键词自动写入标签 / 类型 / 演员 / 制片。<br>' +
            '每条规则可以自由指定 <b>生效的媒体库</b>；不勾选任何媒体库表示在所有媒体库生效。';
        wrap.appendChild(sub);

        // 语法说明集中放在顶部，规则卡片里不再重复
        var help = el('div', 'ft-help');
        help.innerHTML = HELP_HTML;
        wrap.appendChild(help);

        return wrap;
    };

    TaggingPage.prototype.renderSwitches = function () {
        var card = el('div', 'ft-card');
        var head = el('div', 'ft-card-head');
        head.appendChild(el('span', null, '全局选项'));
        card.appendChild(head);

        var body = el('div', 'ft-card-body');
        var self = this;
        var rows = [
            ['Enabled', '启用插件', '关闭后，定时任务与实时监听都不会写入任何内容。'],
            ['EnableRealtimeMonitor', '实时监听新入库条目', '新条目入库时立即处理。大批量导入时建议关闭，改用计划任务。'],
            ['MergeWithExisting', '追加模式（保留已有内容）', '开启时只在原有标签/类型/演员上追加，不删除任何已有内容。'],
            ['VideosOnly', '仅处理视频', '电影 / 剧集 / 家庭视频。关闭后也会处理音乐、书籍等其它类型。'],
            ['DebugLogging', '调试日志', '把每条规则的命中/跳过原因写入 Emby 日志，排错时开启。']
        ];

        rows.forEach(function (r) {
            body.appendChild(self.switchRow(r[0], r[1], r[2]));
        });

        card.appendChild(body);
        return card;
    };

    TaggingPage.prototype.switchRow = function (prop, title, desc) {
        var self = this;
        var row = el('div', 'ft-switch-row');
        var box = el('div');
        var label = el('label');
        var input = document.createElement('input');
        input.type = 'checkbox';
        input.checked = !!self.state.config[prop];
        input.addEventListener('change', function () { self.state.config[prop] = input.checked; });
        label.appendChild(input);
        label.appendChild(el('span', null, title));
        box.appendChild(label);
        box.appendChild(el('div', 'ft-switch-desc', desc));
        row.appendChild(box);
        return row;
    };

    TaggingPage.prototype.renderRuleSection = function () {
        var self = this;
        var card = el('div', 'ft-card');
        var head = el('div', 'ft-card-head');

        var titleWrap = el('div');
        titleWrap.style.cssText = 'display:flex;align-items:center;gap:10px';
        titleWrap.appendChild(el('span', null, '规则列表'));
        titleWrap.appendChild(el('span', 'ft-count', '共 ' + self.state.config.Rules.length + ' 条'));
        head.appendChild(titleWrap);

        var btnWrap = el('div');
        btnWrap.style.cssText = 'display:flex;gap:8px;align-items:center';
        btnWrap.appendChild(el('span', 'ft-hint', '新建：'));

        [0, 1, 2, 3].forEach(function (kind) {
            var b = el('button', 'ft-btn ft-small', '+ ' + KIND_META[kind].label + '规则');
            b.type = 'button';
            b.addEventListener('click', function () {
                self.state.config.Rules.push(newRule(kind));
                self.render();
            });
            btnWrap.appendChild(b);
        });

        head.appendChild(btnWrap);
        card.appendChild(head);

        var body = el('div', 'ft-card-body');

        if (self.state.config.Rules.length === 0) {
            var empty = el('div', 'ft-empty');
            empty.innerHTML = '还没有任何规则。<br>点击右上角的「+ 标签规则」等按钮开始添加。';
            body.appendChild(empty);
        } else {
            self.state.config.Rules.forEach(function (rule, index) {
                body.appendChild(self.renderRule(rule, index));
            });
        }

        card.appendChild(body);
        return card;
    };

    TaggingPage.prototype.renderRule = function (rule, index) {
        var self = this;
        // 服务端回传的 Kind 是枚举名（"Tag"/"Genre"/"Actor"），这里统一成数字
        var selfKind = kindToNumber(rule.Kind);
        var meta = KIND_META[selfKind] || KIND_META[0];
        var card = el('div', 'ft-rule' + (rule.Enabled ? '' : ' ft-rule-disabled'));

        var head = el('div', 'ft-rule-head');

        var kindSelect = document.createElement('span');
        kindSelect.className = 'ft-kind-badge ' + meta.cls;
        kindSelect.textContent = meta.label + '规则';
        kindSelect.title = '种类在创建时确定，不可修改；如需其它种类请新建规则';
        head.appendChild(kindSelect);

        var nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.className = 'ft-name-slot';
        nameInput.placeholder = '规则名称（可选，仅用于识别）';
        nameInput.value = rule.Name || '';
        nameInput.addEventListener('input', function () { rule.Name = nameInput.value; });
        head.appendChild(nameInput);

        var enableLabel = el('label');
        enableLabel.style.cssText = 'display:flex;align-items:center;gap:6px;font-size:13px;cursor:pointer';
        var enableCb = document.createElement('input');
        enableCb.type = 'checkbox';
        enableCb.checked = !!rule.Enabled;
        enableCb.addEventListener('change', function () {
            rule.Enabled = enableCb.checked;
            self.render();
        });
        enableLabel.appendChild(enableCb);
        enableLabel.appendChild(el('span', null, '启用'));
        head.appendChild(enableLabel);

        var actions = el('div', 'ft-actions');

        var up = el('button', 'ft-btn ft-small', '↑');
        up.type = 'button';
        up.title = '上移';
        up.disabled = index === 0;
        up.addEventListener('click', function () { self.swap(index, index - 1); });
        actions.appendChild(up);

        var down = el('button', 'ft-btn ft-small', '↓');
        down.type = 'button';
        down.title = '下移';
        down.disabled = index === self.state.config.Rules.length - 1;
        down.addEventListener('click', function () { self.swap(index, index + 1); });
        actions.appendChild(down);

        var del = el('button', 'ft-btn ft-small ft-danger', '删除');
        del.type = 'button';
        del.addEventListener('click', function () {
            self.state.config.Rules.splice(index, 1);
            self.render();
        });
        actions.appendChild(del);

        head.appendChild(actions);
        card.appendChild(head);

        var body = el('div', 'ft-rule-body');

        // 规则主体：映射表 / 正则共用同一个输入框
        body.appendChild(this.renderMapping(rule));

        // 同时写入：同一关键词也可以触发其它种类
        body.appendChild(this.renderAlsoKinds(rule));

        body.appendChild(this.renderLibraryPicker(rule, false));
        body.appendChild(this.renderLibraryPicker(rule, true));

        card.appendChild(body);
        return card;
    };

    /**
     * 主体输入框：映射表与正则共用。
     *
     * 正则开启时不额外多一个框 —— 同一个框改成放正则，
     * 因为两者本就互斥（正则启用后映射表不生效），
     * 分开反而会让人以为要填两处。
     *
     * 正则开关放在"关键词 = 目标值"标签的同一行右侧，
     * 避免单独占一整行留下大片空白。
     */
    TaggingPage.prototype.renderMapping = function (rule) {
        var meta = KIND_META[rule.Kind] || KIND_META[0];

        var wrap = el('div', 'ft-field');

        // ---- 标题行：左标题 + 右正则开关 ----
        var headRow = el('div', 'ft-mapping-head');

        var title = el('label', 'ft-label', '关键词 = 目标值');
        title.style.marginBottom = '0';
        headRow.appendChild(title);

        var useRegex = !!(rule.Pattern && rule.Pattern.trim());

        var toggle = el('label', 'ft-regex-toggle');
        var cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.checked = useRegex;
        toggle.appendChild(cb);
        toggle.appendChild(el('span', null, '使用正则'));
        headRow.appendChild(toggle);

        wrap.appendChild(headRow);

        // ---- 主体框 ----
        var textarea = document.createElement('textarea');
        textarea.className = 'ft-mapping';
        textarea.rows = 1;
        textarea.spellcheck = false;

        var status = el('div', 'ft-hint');

        var MAPPING_PLACEHOLDER = '"2160p"|"4K"|"UHD"="4K", "1080p"="1080P", "国语"|"粤语"="中文配音"';
        var REGEX_PLACEHOLDER = '^(?<actor>[\\u4e00-\\u9fa5]{2,4})\\.';

        function applyModeUi() {
            if (cb.checked) {
                textarea.placeholder = REGEX_PLACEHOLDER;
                if (!textarea.value.trim() || textarea.value === (rule.MappingText || '')) {
                    // 切到正则时把正则内容放进同一个框
                    textarea.value = rule.Pattern || '';
                }
            } else {
                textarea.placeholder = MAPPING_PLACEHOLDER;
                textarea.value = rule.MappingText || legacyToMapping(rule);
                rule.Pattern = '';
            }
        }

        function refresh() {
            if (cb.checked) {
                rule.Pattern = textarea.value;
                rule.MappingText = '';
                status.textContent = textarea.value.trim()
                    ? '正则模式：本条规则改用正则匹配，已忽略上面的映射表'
                    : '正则模式：请填写正则表达式';
                status.className = 'ft-hint';
            } else {
                rule.MappingText = textarea.value;
                var parsed = parseMapping(textarea.value);
                if (!textarea.value.trim()) {
                    status.textContent = meta.hint || '';
                    status.className = 'ft-hint';
                } else if (parsed.errors.length) {
                    status.textContent = parsed.errors[0];
                    status.className = 'ft-hint ft-warn';
                } else {
                    status.textContent = '已识别 ' + parsed.count + ' 条映射';
                    status.className = 'ft-hint';
                }
            }
        }

        cb.addEventListener('change', function () {
            applyModeUi();
            refresh();
            if (cb.checked) textarea.focus();
        });

        textarea.addEventListener('input', refresh);

        if (useRegex) {
            textarea.value = rule.Pattern || '';
            textarea.placeholder = REGEX_PLACEHOLDER;
        } else {
            textarea.value = rule.MappingText || legacyToMapping(rule);
            textarea.placeholder = MAPPING_PLACEHOLDER;
        }

        wrap.appendChild(textarea);
        wrap.appendChild(status);

        setTimeout(refresh, 0);
        return wrap;
    };

    /**
     * "同时写入"区域：允许一条规则命中后写入多个种类。
     * 自身种类固定勾选且不可取消。
     */
    TaggingPage.prototype.renderAlsoKinds = function (rule) {
        var self = this;
        var wrap = el('div', 'ft-also');

        wrap.appendChild(el('span', 'ft-also-title', '命中后同时写入：'));

        if (!rule.Kinds) rule.Kinds = [];

        var selfKind = kindToNumber(rule.Kind);

        [0, 1, 2, 3].forEach(function (k) {
            var isSelf = (k === selfKind);
            var label = el('label');

            var cb = document.createElement('input');
            cb.type = 'checkbox';
            // Kinds 里可能存着枚举名字符串，这里一并用数字比较
            cb.checked = isSelf || (rule.Kinds || []).some(function (x) { return kindToNumber(x) === k; });
            cb.disabled = isSelf;

            cb.addEventListener('change', function () {
                if (isSelf) return;
                var arr = rule.Kinds || (rule.Kinds = []);
                // 统一成名字数组，避免数字/字符串混存
                var names = arr.map(kindToName);
                var name = kindToName(k);
                var i = names.indexOf(name);
                if (cb.checked) { if (i < 0) names.push(name); }
                else { if (i >= 0) names.splice(i, 1); }
                rule.Kinds = names;
            });

            label.appendChild(cb);
            label.appendChild(el('span', null, KIND_META[k].label));
            if (isSelf) label.appendChild(el('span', 'ft-self', '(本规则)'));
            wrap.appendChild(label);
        });

        return wrap;
    };


    /**
     * 前端预校验，规则与后端 MappingParser 保持一致：
     * 逗号或换行分隔条目，等号分隔键值，竖线分隔同侧多个值，
     * 双引号可包裹含特殊字符的内容。
     */
    function parseMapping(text) {
        if (!text || !text.trim()) return { count: 0, errors: [] };

        var errors = [];
        var count = 0;
        var inQuote = false;
        var buf = '';
        var segments = [];

        for (var i = 0; i < text.length; i++) {
            var ch = text.charAt(i);

            if (ch === '"') { inQuote = !inQuote; buf += ch; continue; }

            if (!inQuote && (ch === ',' || ch === '，' || ch === '\n' || ch === '\r')) {
                segments.push(buf);
                buf = '';
                continue;
            }
            buf += ch;
        }
        segments.push(buf);

        for (var s = 0; s < segments.length; s++) {
            var seg = segments[s].trim();
            if (!seg) continue;
            if (seg.charAt(0) === '#' || seg.indexOf('//') === 0) continue;

            // 找引号外的第一个等号
            var idx = -1;
            var q = false;
            for (var j = 0; j < seg.length; j++) {
                var c = seg.charAt(j);
                if (c === '"') { q = !q; continue; }
                if (q) continue;
                if (c === '=' || c === '＝' || c === '：') { idx = j; break; }
            }

            if (idx < 0) {
                errors.push('「' + seg + '」缺少等号，应为 "关键词"="目标值"');
                continue;
            }

            var left = seg.substring(0, idx).trim();
            if (!left) {
                errors.push('有一段等号左边是空的：' + seg);
                continue;
            }

            // 强制引号：每个关键词都必须是 "..." 形式
            if (!allQuoted(left)) {
                errors.push('关键词「' + left + '」缺少双引号，应为 "' + left + '"');
                continue;
            }

            var right = seg.substring(idx + 1).trim();
            // 目标值可以为空；非空时也必须带引号
            if (right && !allQuoted(right)) {
                errors.push('目标值「' + right + '」缺少双引号，应为 "' + right + '"');
                continue;
            }

            count++;
        }

        if (inQuote) errors.unshift('双引号没有闭合');

        return { count: count, errors: errors };
    }

    /** 判断一侧是否全部由 "..." 组成（用 | 或 、 分隔）。 */
    function allQuoted(side) {
        if (!side) return true;
        var parts = side.split(/[|、]/);
        for (var i = 0; i < parts.length; i++) {
            var p = parts[i].trim();
            if (!p) continue;
            if (p.length < 2 || p.charAt(0) !== '"' || p.charAt(p.length - 1) !== '"') return false;
        }
        return true;
    }

    /**
     * 老配置迁移：把 Keywords/TargetValues 转成映射表文本，
     * 避免升级后用户已保存的规则显示为空。
     */
    function legacyToMapping(rule) {
        if (!rule.Keywords) return '';
        var keys = rule.Keywords.split(/[|,，、]/)
            .map(function (x) { return x.trim(); })
            .filter(function (x) { return x.length > 0; })
            .map(function (x) { return '"' + x + '"'; });
        if (!keys.length) return '';
        var t = (rule.TargetValues || '').trim();
        return keys.join('|') + '=' + (t ? '"' + t + '"' : '');
    }

    TaggingPage.prototype.field = function (labelText, rule, prop, placeholder, hint) {
        var wrap = el('div', 'ft-field');
        wrap.appendChild(el('label', 'ft-label', labelText));

        var input = document.createElement('input');
        input.type = 'text';
        input.placeholder = placeholder || '';
        input.value = rule[prop] || '';
        input.addEventListener('input', function () { rule[prop] = input.value; });
        wrap.appendChild(input);

        if (hint) wrap.appendChild(el('div', 'ft-hint', hint));
        return wrap;
    };

    /**
     * 媒体库选择：下拉式多选。
     *
     * 之前用一整块复选框列表占掉半屏，改成折叠下拉：
     * 收起时只占一行，显示已选内容；点开才展开勾选。
     */
    TaggingPage.prototype.renderLibraryPicker = function (rule, exclude) {
        var self = this;
        var prop = exclude ? 'ExcludedLibraries' : 'Libraries';
        var wrap = el('div', 'ft-field ft-libfield');

        var head = el('div', 'ft-libfield-head');

        var label = el('label', 'ft-label', exclude ? '排除的媒体库' : '生效的媒体库');
        label.style.marginBottom = '0';
        head.appendChild(label);

        // 当前选中情况的摘要（收起时可见）
        var summary = el('span', 'ft-lib-badge');

        var btn = el('button', 'ft-lib-toggle', '选择 ▾');
        btn.type = 'button';

        head.appendChild(summary);
        head.appendChild(btn);
        wrap.appendChild(head);

        // 下拉面板
        var panel = el('div', 'ft-libs');
        panel.style.display = 'none';

        if (self.state.libraries.length === 0) {
            panel.appendChild(el('div', 'ft-libs-empty', '未检测到媒体库，请先在 Emby 中创建媒体库。'));
        } else {
            self.state.libraries.forEach(function (lib) {
                var item = el('label', 'ft-lib-item');
                var cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.checked = (rule[prop] || []).indexOf(lib) >= 0;

                cb.addEventListener('change', function () {
                    var arr = rule[prop] || (rule[prop] = []);
                    var i = arr.indexOf(lib);
                    if (cb.checked) { if (i < 0) arr.push(lib); }
                    else { if (i >= 0) arr.splice(i, 1); }
                    self.updateSummary(wrap, rule, prop, exclude);
                });

                item.appendChild(cb);
                item.appendChild(el('span', null, lib));
                panel.appendChild(item);
            });
        }

        btn.addEventListener('click', function () {
            var open = panel.style.display !== 'none';
            panel.style.display = open ? 'none' : 'block';
            btn.textContent = open ? '选择 ▾' : '收起 ▴';
        });

        wrap.appendChild(panel);
        this.updateSummary(wrap, rule, prop, exclude);

        return wrap;
    };

    TaggingPage.prototype.updateSummary = function (wrap, rule, prop, exclude) {
        var badge = wrap.querySelector('.ft-lib-badge');
        if (!badge) return;
        var arr = rule[prop] || [];

        if (exclude) {
            if (arr.length === 0) {
                badge.textContent = '未排除';
                badge.className = 'ft-lib-badge ft-none';
            } else {
                badge.textContent = '排除 ' + arr.join('、');
                badge.className = 'ft-lib-badge ft-warn';
            }
            return;
        }

        if (arr.length === 0) {
            badge.textContent = '全部媒体库';
            badge.className = 'ft-lib-badge ft-none';
        } else {
            badge.textContent = arr.join('、');
            badge.className = 'ft-lib-badge';
        }
    };

    TaggingPage.prototype.swap = function (i, j) {
        if (j < 0 || j >= this.state.config.Rules.length) return;
        var tmp = this.state.config.Rules[i];
        this.state.config.Rules[i] = this.state.config.Rules[j];
        this.state.config.Rules[j] = tmp;
        this.render();
    };

    TaggingPage.prototype.renderFooter = function () {
        var self = this;
        var wrap = el('div');
        wrap.style.cssText = 'display:flex;gap:12px;align-items:center';

        var save = el('button', 'ft-btn ft-primary', '保存配置');
        save.type = 'button';
        save.addEventListener('click', function () { self.doSave(); });
        wrap.appendChild(save);

        var reload = el('button', 'ft-btn', '重新加载');
        reload.type = 'button';
        reload.addEventListener('click', function () { self.load(true); });
        wrap.appendChild(reload);

        wrap.appendChild(el('span', 'ft-hint', '保存后立即生效，无需重启 Emby。'));
        return wrap;
    };

    TaggingPage.prototype.doSave = function () {
        var self = this;
        var payload = {
            Enabled: !!self.state.config.Enabled,
            EnableRealtimeMonitor: !!self.state.config.EnableRealtimeMonitor,
            MergeWithExisting: !!self.state.config.MergeWithExisting,
            VideosOnly: !!self.state.config.VideosOnly,
            DebugLogging: !!self.state.config.DebugLogging,
            Rules: (self.state.config.Rules || []).map(function (r) {
                return {
                    // 统一输出枚举名，与 Emby 的序列化格式保持一致
                    Kind: kindToName(r.Kind),
                    Kinds: (r.Kinds || []).map(kindToName),
                    Enabled: !!r.Enabled,
                    Name: r.Name || '',
                    MappingText: r.MappingText || legacyToMapping(r),
                    Pattern: r.Pattern || '',
                    Libraries: r.Libraries || [],
                    ExcludedLibraries: r.ExcludedLibraries || []
                };
            })
        };

        apiSave(payload)
            .then(function (res) {
                if (res && res.Success) toast('已保存，共 ' + res.Count + ' 条规则', true);
                else toast('保存失败：' + ((res && res.Message) || '未知错误'), false);
            })
            .catch(function (err) { toast('保存失败：' + err.message, false); });
    };

    TaggingPage.prototype.load = function (silent) {
        var self = this;
        var root = this.root();
        if (!root) return;

        if (!silent) root.innerHTML = '<div class="ft-loading">正在加载配置…</div>';

        apiGet()
            .then(function (res) {
                self.state.config = {
                    Enabled: res.Enabled,
                    EnableRealtimeMonitor: res.EnableRealtimeMonitor,
                    MergeWithExisting: res.MergeWithExisting,
                    VideosOnly: res.VideosOnly,
                    DebugLogging: res.DebugLogging,
                    Rules: res.Rules || []
                };

                self.state.config.Rules.forEach(function (r) {
                    if (!r.Libraries) r.Libraries = [];
                    if (!r.ExcludedLibraries) r.ExcludedLibraries = [];
                    if (!r.Kinds) r.Kinds = [];
                });

                self.state.libraries = res.Libraries || [];
                self.render();

                if (silent) toast('已重新加载', true);
            })
            .catch(function (err) {
                root.innerHTML = '<div class="ft-empty">加载配置失败：' + err.message +
                    '<br><br>请确认已登录 Emby 仪表板并具有管理员权限。</div>';
            });
    };

    // ---------------- 页面绑定 ----------------

    function getPage(view) {
        if (!view._ftPage) {
            view._ftPage = new TaggingPage(view);
        }
        return view._ftPage;
    }

    return function (view, params) {
        var page = getPage(view);
        page.init();

        // Emby 在视图销毁时调用
        view.addEventListener('viewdestroy', function () {
            view._ftPage = null;
        });
    };
});
