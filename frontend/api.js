const HTTP = Object.freeze({
  noContent: 204, badRequest: 400, unauthorized: 401, forbidden: 403,
  notFound: 404, conflict: 409, tooManyRequests: 429, serverError: 500
}); // 集中说明响应状态码，避免散落在业务模块中。

/** 表达可展示的请求错误，状态码供身份模块处理会话到期。 */
export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

/** 统一 Cookie、防伪和错误协议，依赖可替换的 fetch 供网络边界测试。 */
export function createApiClient(config, { fetchImpl = globalThis.fetch, onUnauthorized = () => {},
  getSessionVersion = () => undefined } = {}) {
  const base = config.apiBase.replace(/\/$/, '');
  // 前后端 Cookie 必须同源；配置不能把私人请求发送到外部域名。
  if (!base.startsWith('/') || base.startsWith('//') || /[?#]/.test(base))
    throw new Error('apiBase 必须是同源路径，例如 /api。');

  /** 按合同解析请求，不缓存响应或把私人资料写入浏览器持久存储。 */
  async function request(path, { method = 'GET', body } = {}) {
    const startedVersion = getSessionVersion();
    const headers = { Accept: 'application/json' };
    const writing = method !== 'GET' && method !== 'HEAD';
    if (writing) {
      // 登录前后防伪令牌绑定不同身份，每次写入重新取令牌，提交本身不自动重试。
      const csrf = await request('/auth/csrf');
      if (startedVersion !== getSessionVersion()) throw new ApiError(HTTP.unauthorized, '账号状态已变化，请重新操作。');
      headers['X-CSRF-TOKEN'] = csrf.token;
    }
    const multipart = body instanceof FormData;
    if (body !== undefined && !multipart) headers['Content-Type'] = 'application/json';
    let response;
    try {
      response = await fetchImpl(base + path, {
        method, headers, credentials: 'include', cache: 'no-store',
        body: body === undefined ? undefined : multipart ? body : JSON.stringify(body)
      });
    } catch {
      throw new ApiError(0, '暂时连接不上服务，请稍后重试。'); // 零表示请求未取得 HTTP 响应。
    }
    if (response.status === HTTP.noContent) return null;
    const data = response.headers.get('content-type')?.includes('json') ? await response.json() : {};
    if (!response.ok) {
      // 登录/会话恢复的正常未认证交给身份模块；私人资源的 401 立即清空显示状态。
      if (response.status === HTTP.unauthorized && path !== '/auth/login' && path !== '/auth/me' &&
        startedVersion === getSessionVersion()) onUnauthorized();
      const defaults = {
        [HTTP.badRequest]: '请检查填写内容后重试。',
        [HTTP.unauthorized]: path === '/auth/login' ? '账号或密码不正确，或账号暂时不可用。' : '登录已过期，请重新登录。',
        [HTTP.forbidden]: '当前操作无法完成，请刷新后重试。',
        [HTTP.notFound]: '内容已下架或你无权查看，请刷新列表。',
        [HTTP.conflict]: '内容已处理或名额不足，请刷新后重试。',
        [HTTP.tooManyRequests]: '操作太频繁，请稍后再试。'
      };
      // 只展示预期业务错误；服务器故障的 detail 可能包含内部信息。
      const message = response.status >= HTTP.serverError ? '服务暂时不可用，请稍后重试。'
        : data.message || (data.errors && Object.values(data.errors).flat().join(' ')) || defaults[response.status] || '操作未完成，请重试。';
      throw new ApiError(response.status, message);
    }
    return data;
  }

  /** 只接收 API 生成的照片路径，避免把任意外链或脚本地址作为图片源。 */
  function photoUrl(url) {
    return /^\/api\/animals\/\d+\/photos\/\d+$/.test(url) ? base + url.slice('/api'.length) : '';
  }
  return { request, photoUrl };
}
