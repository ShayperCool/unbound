// The Unity TLS binding's C functions (Unity.TLS.LowLevel.Binding, which Unity Transport drives for
// its TLS and DTLS layers) over Mbed TLS 3.6 (native/mbedtls), the library the binding is a layer
// over. What each function answers is measured: tests/projects/unitytls (Unity 6000.0.71f1).
//
// The configuration and client structures are the binding's (their managed declarations in
// managed/UnityEngine/TLS/Binding.cs have the same layout). A client is configured by `init`
// from its configuration, which it keeps a pointer to; the handshake moves one Mbed TLS step a
// call when the configuration asks for it; failures leave the client failed with an error state
// (the code and its detail: the peer certificate's verification flags, or Mbed TLS's error).
#define MBEDTLS_ALLOW_PRIVATE_ACCESS
#include <mbedtls/ctr_drbg.h>
#include <mbedtls/debug.h>
#include <mbedtls/entropy.h>
#include <mbedtls/error.h>
#include <mbedtls/pk.h>
#include <mbedtls/ssl.h>
#include <mbedtls/timing.h>
#include <mbedtls/x509_crt.h>
#if defined(MBEDTLS_PSA_CRYPTO_C)
#include <psa/crypto.h>
#endif
#include <pthread.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#if defined(__APPLE__)
#include <Security/Security.h>
#endif

#define UNITYTLS_API __attribute__((visibility("default")))

enum {
    UNITYTLS_SUCCESS = 0,
    UNITYTLS_INVALID_ARGUMENT = 1,
    UNITYTLS_INVALID_FORMAT = 2,
    UNITYTLS_INVALID_STATE = 4,
    UNITYTLS_OUT_OF_MEMORY = 6,
    UNITYTLS_INTERNAL_ERROR = 7,
    UNITYTLS_SSL_ERROR = 13,
    UNITYTLS_USER_WOULD_BLOCK = 1048577,
    UNITYTLS_USER_READ_FAILED = 1048580,
    UNITYTLS_USER_WRITE_FAILED = 1048581,
    UNITYTLS_HANDSHAKE_STEP = 1048584,
};

enum { ROLE_NONE = 0, ROLE_SERVER = 1, ROLE_CLIENT = 2 };
enum { PROTOCOL_STREAM = 0, PROTOCOL_DATAGRAM = 1 };
enum { STATE_NONE = 0, STATE_INIT = 1, STATE_HANDSHAKE = 2, STATE_MESSAGING = 3, STATE_FAIL = 64 };

// The verification result of a peer certificate not verified (yet): the binding's "not done"
// flag; verified certificates report Mbed TLS's flags (the not-trusted and name-mismatch bits
// are the binding's too).
#define X509_VERIFY_NOT_DONE 0x80000000u

typedef struct {
    const uint8_t *dataPtr;
    size_t dataLen;
} unitytls_dataRef;

typedef int (*unitytls_data_callback)(void *, uint8_t *, size_t, uint32_t);
typedef void (*unitytls_log_callback)(int, const char *, size_t, const char *, const char *, size_t);

typedef struct {
    unitytls_dataRef caPEM;
    unitytls_dataRef serverPEM;
    unitytls_dataRef privateKeyPEM;
    uint32_t clientAuth;
    uint32_t transportProtocol;
    unitytls_dataRef psk;
    unitytls_dataRef pskIdentity;
    void *onDataCB;
    unitytls_data_callback dataSendCB;
    unitytls_data_callback dataReceiveCB;
    void *dataReceiveTimeoutCB;
    void *transportUserData;
    void *applicationUserData;
    int32_t handshakeReturnsOnStep;
    int32_t handshakeReturnsIfWouldBlock;
    uint32_t ssl_read_timeout_ms;
    const char *hostname;
    uint32_t tracelevel;
    unitytls_log_callback logCallback;
    uint32_t ssl_handshake_timeout_min;
    uint32_t ssl_handshake_timeout_max;
    uint16_t mtu;
} unitytls_client_config;

typedef struct {
    uint32_t role;
    uint32_t state;
    uint32_t handshakeState;
    void *ctx;
    unitytls_client_config *config;
    void *internalCtx;
} unitytls_client;

// What `init` makes (the client's `ctx`).
typedef struct {
    mbedtls_ssl_context ssl;
    mbedtls_ssl_config conf;
    mbedtls_x509_crt ca;
    mbedtls_x509_crt own;
    mbedtls_pk_context key;
    mbedtls_entropy_context entropy;
    mbedtls_ctr_drbg_context drbg;
    mbedtls_timing_delay_context timer;
    int *suites; // the client's added cipher suites when init ran (Mbed TLS keeps the pointer)
} tls_context;

// The client's own records (its `internalCtx`).
typedef struct {
    int *suites; // added cipher suites, 0-terminated as Mbed TLS takes them
    int suite_count;
    uint32_t error_code;
    uint64_t error_detail;
} client_records;

_Static_assert(sizeof(unitytls_client_config) == 192, "the binding's configuration is 192 bytes");
_Static_assert(sizeof(unitytls_client) == 40, "the binding's client is 40 bytes");

static client_records *records(unitytls_client *client) { return (client_records *)client->internalCtx; }

static void fail(unitytls_client *client, uint32_t code, uint64_t detail) {
    client->state = STATE_FAIL;
    records(client)->error_code = code;
    records(client)->error_detail = detail;
}

UNITYTLS_API void unitytls_client_init_config(unitytls_client_config *config) {
    memset(config, 0, sizeof(*config));
    config->clientAuth = 1;
    config->transportProtocol = PROTOCOL_DATAGRAM;
    config->handshakeReturnsOnStep = 1;
    config->handshakeReturnsIfWouldBlock = 1;
    config->tracelevel = 2;
    config->ssl_handshake_timeout_min = 1000;
    config->ssl_handshake_timeout_max = 60000;
}

UNITYTLS_API unitytls_client *unitytls_client_create(uint32_t role, unitytls_client_config *config) {
    unitytls_client *client = calloc(1, sizeof(unitytls_client));
    client_records *own = calloc(1, sizeof(client_records));
    if (client == NULL || own == NULL) {
        free(client);
        free(own);
        return NULL;
    }
    client->role = role;
    client->state = STATE_NONE;
    client->config = config;
    client->internalCtx = own;
    return client;
}

// A transport without user data fails without its callbacks being called (measured: the first
// handshake call fails, no record sent).
static int bio_send(void *user, const unsigned char *data, size_t length) {
    unitytls_client *client = user;
    if (client->config->dataSendCB == NULL || client->config->transportUserData == NULL)
        return MBEDTLS_ERR_SSL_INTERNAL_ERROR;
    return client->config->dataSendCB(client->config->transportUserData, (uint8_t *)data, length, 0);
}

static int bio_receive(void *user, unsigned char *data, size_t length) {
    unitytls_client *client = user;
    if (client->config->dataReceiveCB == NULL || client->config->transportUserData == NULL)
        return MBEDTLS_ERR_SSL_INTERNAL_ERROR;
    return client->config->dataReceiveCB(client->config->transportUserData, data, length, 0);
}

static void debug(void *user, int level, const char *file, int line, const char *message) {
    unitytls_client *client = user;
    if (client->config->logCallback != NULL)
        client->config->logCallback(level, file, (size_t)line, "", message, strlen(message));
}

static int has(unitytls_dataRef data) { return data.dataPtr != NULL && data.dataLen > 0; }

// A PEM block, NUL-terminated as Mbed TLS reads PEM (the binding's data carries no terminator).
static unsigned char *terminated(unitytls_dataRef data) {
    unsigned char *copy = malloc(data.dataLen + 1);
    if (copy == NULL) return NULL;
    memcpy(copy, data.dataPtr, data.dataLen);
    copy[data.dataLen] = 0;
    return copy;
}

static int parse_certificates(mbedtls_x509_crt *target, unitytls_dataRef data) {
    unsigned char *copy = terminated(data);
    if (copy == NULL) return MBEDTLS_ERR_X509_ALLOC_FAILED;
    int ret = mbedtls_x509_crt_parse(target, copy, data.dataLen + 1);
    free(copy);
    return ret;
}

static int parse_key(mbedtls_pk_context *target, unitytls_dataRef data, mbedtls_ctr_drbg_context *drbg) {
    unsigned char *copy = terminated(data);
    if (copy == NULL) return MBEDTLS_ERR_PK_ALLOC_FAILED;
    int ret = mbedtls_pk_parse_key(target, copy, data.dataLen + 1, NULL, 0, mbedtls_ctr_drbg_random, drbg);
    free(copy);
    return ret;
}

// The system's trusted roots, which a client without a CA verifies its server against (measured:
// such a client verifies public servers, and an explicit CA replaces them). Read once: the
// keychain's anchors on macOS, the system's PEM bundle elsewhere.
static mbedtls_x509_crt s_system_roots;
static pthread_once_t s_system_roots_once = PTHREAD_ONCE_INIT;

static void load_system_roots(void) {
    mbedtls_x509_crt_init(&s_system_roots);
#if defined(__APPLE__)
    CFArrayRef anchors = NULL;
    if (SecTrustCopyAnchorCertificates(&anchors) != errSecSuccess || anchors == NULL) return;
    for (CFIndex i = 0; i < CFArrayGetCount(anchors); i++) {
        CFDataRef der = SecCertificateCopyData((SecCertificateRef)CFArrayGetValueAtIndex(anchors, i));
        if (der == NULL) continue;
        mbedtls_x509_crt_parse_der(&s_system_roots, CFDataGetBytePtr(der), (size_t)CFDataGetLength(der));
        CFRelease(der);
    }
    CFRelease(anchors);
#else
    static const char *bundles[] = {"/etc/ssl/certs/ca-certificates.crt", "/etc/pki/tls/certs/ca-bundle.crt",
                                    "/etc/ssl/ca-bundle.pem", "/etc/pki/tls/cacert.pem", "/etc/ssl/cert.pem"};
    for (size_t i = 0; i < sizeof(bundles) / sizeof(bundles[0]); i++)
        if (mbedtls_x509_crt_parse_file(&s_system_roots, bundles[i]) >= 0 && s_system_roots.version != 0) break;
#endif
}

#if defined(MBEDTLS_PSA_CRYPTO_C)
static psa_status_t s_psa_status = PSA_ERROR_BAD_STATE;
static pthread_once_t s_psa_once = PTHREAD_ONCE_INIT;
static void init_psa(void) { s_psa_status = psa_crypto_init(); }
#endif

// The signature algorithms a ClientHello offers, in the binding's order (measured: SHA-512, then
// SHA-384, then SHA-256; ECDSA before RSA).
static const uint16_t s_sig_algs[] = {0x0603, 0x0601, 0x0503, 0x0501, 0x0403, 0x0401, MBEDTLS_TLS1_3_SIG_NONE};

static void free_context(tls_context *tls) {
    if (tls == NULL) return;
    mbedtls_ssl_free(&tls->ssl);
    mbedtls_ssl_config_free(&tls->conf);
    mbedtls_x509_crt_free(&tls->ca);
    mbedtls_x509_crt_free(&tls->own);
    mbedtls_pk_free(&tls->key);
    mbedtls_ctr_drbg_free(&tls->drbg);
    mbedtls_entropy_free(&tls->entropy);
    free(tls->suites);
    free(tls);
}

// Fails a client's init: the client stays without a context, failed with the error.
static int init_failed(unitytls_client *client, tls_context *tls, uint32_t code, int detail) {
    free_context(tls);
    fail(client, code, (uint64_t)(int64_t)detail);
    return UNITYTLS_SSL_ERROR;
}

UNITYTLS_API int unitytls_client_init(unitytls_client *client) {
    if (client == NULL || client->config == NULL) return UNITYTLS_INVALID_ARGUMENT;
    // A client is made once (unmeasured: a second init is refused, the first context kept).
    if (client->ctx != NULL) return UNITYTLS_INVALID_STATE;
    unitytls_client_config *config = client->config;
    int server = client->role == ROLE_SERVER;
#if defined(MBEDTLS_PSA_CRYPTO_C)
    pthread_once(&s_psa_once, init_psa);
    if (s_psa_status != PSA_SUCCESS) return init_failed(client, NULL, UNITYTLS_INTERNAL_ERROR, 0);
#endif
    // A server needs its certificate and key (measured: a PSK server without them does not start).
    if (server && !(has(config->serverPEM) && has(config->privateKeyPEM)))
        return init_failed(client, NULL, UNITYTLS_INVALID_ARGUMENT, 0);
    tls_context *tls = calloc(1, sizeof(tls_context));
    if (tls == NULL) return init_failed(client, NULL, UNITYTLS_OUT_OF_MEMORY, 0);
    mbedtls_ssl_init(&tls->ssl);
    mbedtls_ssl_config_init(&tls->conf);
    mbedtls_x509_crt_init(&tls->ca);
    mbedtls_x509_crt_init(&tls->own);
    mbedtls_pk_init(&tls->key);
    mbedtls_entropy_init(&tls->entropy);
    mbedtls_ctr_drbg_init(&tls->drbg);

    int ret = mbedtls_ctr_drbg_seed(&tls->drbg, mbedtls_entropy_func, &tls->entropy, NULL, 0);
    if (ret != 0) return init_failed(client, tls, UNITYTLS_INTERNAL_ERROR, ret);
    // Certificates and keys that do not parse: their Mbed TLS error.
    if (has(config->caPEM) && (ret = parse_certificates(&tls->ca, config->caPEM)) != 0)
        return init_failed(client, tls, UNITYTLS_INVALID_FORMAT, ret);
    if (has(config->serverPEM) && (ret = parse_certificates(&tls->own, config->serverPEM)) != 0)
        return init_failed(client, tls, UNITYTLS_INVALID_FORMAT, ret);
    if (has(config->privateKeyPEM) && (ret = parse_key(&tls->key, config->privateKeyPEM, &tls->drbg)) != 0)
        return init_failed(client, tls, UNITYTLS_INVALID_FORMAT, ret);

    int datagram = config->transportProtocol == PROTOCOL_DATAGRAM;
    ret = mbedtls_ssl_config_defaults(&tls->conf, server ? MBEDTLS_SSL_IS_SERVER : MBEDTLS_SSL_IS_CLIENT,
                                      datagram ? MBEDTLS_SSL_TRANSPORT_DATAGRAM : MBEDTLS_SSL_TRANSPORT_STREAM,
                                      MBEDTLS_SSL_PRESET_DEFAULT);
    if (ret != 0) return init_failed(client, tls, UNITYTLS_INTERNAL_ERROR, ret);
    // TLS 1.2 and DTLS 1.2 (the handshake states measured are TLS 1.2's).
    mbedtls_ssl_conf_max_tls_version(&tls->conf, MBEDTLS_SSL_VERSION_TLS1_2);
    mbedtls_ssl_conf_min_tls_version(&tls->conf, MBEDTLS_SSL_VERSION_TLS1_2);
    mbedtls_ssl_conf_rng(&tls->conf, mbedtls_ctr_drbg_random, &tls->drbg);
    mbedtls_ssl_conf_sig_algs(&tls->conf, s_sig_algs);
    // The log callback gets Mbed TLS's messages up to the configuration's trace level (unmeasured:
    // the binding's own messages and their function names; Mbed TLS's threshold is process-wide).
    if (config->logCallback != NULL) {
        mbedtls_ssl_conf_dbg(&tls->conf, debug, client);
        mbedtls_debug_set_threshold((int)config->tracelevel);
    }
    // A client always verifies the server (measured: without a CA it fails as not trusted); a
    // server asks for the client's certificate as the configuration says.
    int authmode = MBEDTLS_SSL_VERIFY_REQUIRED;
    if (server)
        authmode = config->clientAuth == 0   ? MBEDTLS_SSL_VERIFY_NONE
                   : config->clientAuth == 2 ? MBEDTLS_SSL_VERIFY_REQUIRED
                                             : MBEDTLS_SSL_VERIFY_OPTIONAL;
    mbedtls_ssl_conf_authmode(&tls->conf, authmode);
    if (!server && !has(config->caPEM)) {
        pthread_once(&s_system_roots_once, load_system_roots);
        mbedtls_ssl_conf_ca_chain(&tls->conf, &s_system_roots, NULL);
    } else {
        mbedtls_ssl_conf_ca_chain(&tls->conf, &tls->ca, NULL);
    }
    if (has(config->serverPEM) && (ret = mbedtls_ssl_conf_own_cert(&tls->conf, &tls->own, &tls->key)) != 0)
        return init_failed(client, tls, UNITYTLS_INTERNAL_ERROR, ret);
    if (has(config->psk) &&
        (ret = mbedtls_ssl_conf_psk(&tls->conf, config->psk.dataPtr, config->psk.dataLen,
                                    has(config->pskIdentity) ? config->pskIdentity.dataPtr : (const unsigned char *)"",
                                    has(config->pskIdentity) ? config->pskIdentity.dataLen : 0)) != 0)
        return init_failed(client, tls, UNITYTLS_INVALID_ARGUMENT, ret);
    client_records *own = records(client);
    if (own->suite_count > 0) {
        tls->suites = malloc(sizeof(int) * (size_t)(own->suite_count + 1));
        if (tls->suites == NULL) return init_failed(client, tls, UNITYTLS_OUT_OF_MEMORY, 0);
        memcpy(tls->suites, own->suites, sizeof(int) * (size_t)(own->suite_count + 1));
        mbedtls_ssl_conf_ciphersuites(&tls->conf, tls->suites);
    }
    if (datagram) {
        mbedtls_ssl_conf_handshake_timeout(&tls->conf, config->ssl_handshake_timeout_min,
                                           config->ssl_handshake_timeout_max);
#if defined(MBEDTLS_SSL_DTLS_HELLO_VERIFY) && defined(MBEDTLS_SSL_SRV_C)
        // No hello verification (measured: a DTLS server answers a first ClientHello); cookies are
        // not set up, so set_cookie_info only gives the server its client's transport id.
        if (server) mbedtls_ssl_conf_dtls_cookies(&tls->conf, NULL, NULL, NULL);
#endif
    }
    mbedtls_ssl_conf_read_timeout(&tls->conf, config->ssl_read_timeout_ms);
    if ((ret = mbedtls_ssl_setup(&tls->ssl, &tls->conf)) != 0)
        return init_failed(client, tls, UNITYTLS_INTERNAL_ERROR, ret);
    // The server's name is checked against the host name; without one it is not (unmeasured: the
    // Editor's answer varies between runs), which Mbed TLS asks to be said explicitly.
    if (!server && (ret = mbedtls_ssl_set_hostname(&tls->ssl, config->hostname)) != 0)
        return init_failed(client, tls, UNITYTLS_INVALID_ARGUMENT, ret);
    mbedtls_ssl_set_bio(&tls->ssl, client, bio_send, bio_receive, NULL);
    if (datagram) {
        mbedtls_ssl_set_timer_cb(&tls->ssl, &tls->timer, mbedtls_timing_set_delay, mbedtls_timing_get_delay);
        if (config->mtu > 0) mbedtls_ssl_set_mtu(&tls->ssl, config->mtu);
        // One record a datagram (measured: a handshake's flights arrive as many datagrams as
        // records, as over a stream).
        mbedtls_ssl_set_datagram_packing(&tls->ssl, 0);
    }
    client->ctx = tls;
    client->state = STATE_INIT;
    return UNITYTLS_SUCCESS;
}

static tls_context *context(unitytls_client *client) { return (tls_context *)client->ctx; }

static int would_block(int ret) {
    return ret == MBEDTLS_ERR_SSL_WANT_READ || ret == MBEDTLS_ERR_SSL_WANT_WRITE ||
           ret == MBEDTLS_ERR_SSL_ASYNC_IN_PROGRESS || ret == MBEDTLS_ERR_SSL_CRYPTO_IN_PROGRESS;
}

// The peer certificate's verification flags (measured: not trusted, name mismatch), or "not done"
// when there was none to verify: Mbed TLS's "not available" (all bits set) and its missing
// certificate flag are the binding's "not done" (measured: a server whose client gave up before
// its certificate, and one whose client sent none).
static uint64_t verification(tls_context *tls) {
    const mbedtls_ssl_session *session = tls->ssl.session_negotiate != NULL ? tls->ssl.session_negotiate : tls->ssl.session;
    if (session == NULL || session->verify_result == 0xFFFFFFFFu || (session->verify_result & MBEDTLS_X509_BADCERT_MISSING))
        return X509_VERIFY_NOT_DONE;
    return session->verify_result;
}

UNITYTLS_API uint32_t unitytls_client_handshake(unitytls_client *client) {
    if (client == NULL || client->ctx == NULL) return UNITYTLS_INVALID_STATE;
    if (client->state != STATE_INIT && client->state != STATE_HANDSHAKE) return UNITYTLS_INVALID_STATE;
    tls_context *tls = context(client);
    client->state = STATE_HANDSHAKE;
    // A handshake that is over is seen by the next call (measured: in steps, the step reaching
    // the end returns a step like the others, and the following call finishes it).
    for (;;) {
        if (mbedtls_ssl_is_handshake_over(&tls->ssl)) {
            client->state = STATE_MESSAGING;
            return UNITYTLS_SUCCESS;
        }
        int ret = mbedtls_ssl_handshake_step(&tls->ssl);
        client->handshakeState = (uint32_t)tls->ssl.state;
        if (ret == 0) {
            if (client->config->handshakeReturnsOnStep) return UNITYTLS_HANDSHAKE_STEP;
            continue;
        }
        if (would_block(ret)) {
            if (client->config->handshakeReturnsIfWouldBlock) return UNITYTLS_USER_WOULD_BLOCK;
            continue;
        }
        fail(client, UNITYTLS_INTERNAL_ERROR, verification(tls));
        return UNITYTLS_SSL_ERROR;
    }
}

UNITYTLS_API uint32_t unitytls_client_send_data(unitytls_client *client, const uint8_t *data, size_t length) {
    if (client == NULL || client->ctx == NULL || client->state != STATE_MESSAGING) return UNITYTLS_USER_WRITE_FAILED;
    size_t written = 0;
    while (written < length) {
        int ret = mbedtls_ssl_write(&context(client)->ssl, data + written, length - written);
        if (ret > 0) {
            written += (size_t)ret;
            continue;
        }
        return would_block(ret) ? UNITYTLS_USER_WOULD_BLOCK : UNITYTLS_USER_WRITE_FAILED;
    }
    return UNITYTLS_SUCCESS;
}

UNITYTLS_API uint32_t unitytls_client_read_data(unitytls_client *client, uint8_t *buffer, size_t length, size_t *read) {
    if (read != NULL) *read = 0;
    if (client == NULL || client->ctx == NULL || client->state != STATE_MESSAGING) return UNITYTLS_USER_READ_FAILED;
    int ret = mbedtls_ssl_read(&context(client)->ssl, buffer, length);
    if (ret > 0) {
        if (read != NULL) *read = (size_t)ret;
        return UNITYTLS_SUCCESS;
    }
    return would_block(ret) ? UNITYTLS_USER_WOULD_BLOCK : UNITYTLS_USER_READ_FAILED;
}

UNITYTLS_API void unitytls_client_add_ciphersuite(unitytls_client *client, uint32_t suite) {
    if (client == NULL) return;
    client_records *own = records(client);
    int *grown = realloc(own->suites, sizeof(int) * (size_t)(own->suite_count + 2));
    if (grown == NULL) return;
    own->suites = grown;
    own->suites[own->suite_count++] = (int)suite;
    own->suites[own->suite_count] = 0;
}

UNITYTLS_API uint32_t unitytls_client_get_ciphersuite(unitytls_client *client, int index) {
    if (client == NULL || index < 0 || index >= records(client)->suite_count) return 0;
    return (uint32_t)records(client)->suites[index];
}

UNITYTLS_API int unitytls_client_get_ciphersuite_cnt(unitytls_client *client) {
    return client == NULL ? 0 : records(client)->suite_count;
}

UNITYTLS_API uint32_t unitytls_client_set_cookie_info(unitytls_client *client, const uint8_t *peer, int length) {
    if (client == NULL || client->ctx == NULL || length < 0) return UNITYTLS_INVALID_ARGUMENT;
#if defined(MBEDTLS_SSL_DTLS_HELLO_VERIFY) && defined(MBEDTLS_SSL_SRV_C)
    if (mbedtls_ssl_set_client_transport_id(&context(client)->ssl, peer, (size_t)length) != 0)
        return UNITYTLS_INVALID_ARGUMENT;
#endif
    return UNITYTLS_SUCCESS;
}

UNITYTLS_API uint32_t unitytls_client_get_handshake_state(unitytls_client *client) {
    return client == NULL ? 0 : client->handshakeState;
}

UNITYTLS_API uint32_t unitytls_client_get_errorsState(unitytls_client *client, uint64_t *reserved) {
    if (client == NULL) return UNITYTLS_INVALID_ARGUMENT;
    if (reserved != NULL) *reserved = records(client)->error_detail;
    return records(client)->error_code;
}

UNITYTLS_API uint32_t unitytls_client_get_state(unitytls_client *client) { return client == NULL ? 0 : client->state; }

UNITYTLS_API uint32_t unitytls_client_get_role(unitytls_client *client) { return client == NULL ? 0 : client->role; }

UNITYTLS_API void unitytls_client_destroy(unitytls_client *client) {
    if (client == NULL) return;
    free_context(context(client));
    client_records *own = records(client);
    if (own != NULL) free(own->suites);
    free(own);
    free(client);
}
