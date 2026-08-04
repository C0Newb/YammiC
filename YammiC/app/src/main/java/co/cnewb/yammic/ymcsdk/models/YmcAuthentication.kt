package co.cnewb.yammic.ymcsdk.models

import java.io.Serializable

/**
 * Authentication data
 */
class YmcAuthentication(ccuId: String, passKey: String) : Serializable {
    val ccuId: String = ccuId;
    val passKey: String = passKey;
}