using System;
using System.Linq;
using System.Threading.Tasks;
using Flow.FCL.Extensions;
using Flow.FCL.Models;
using Flow.FCL.Models.Authz;
using Flow.FCL.Utility;
using Flow.FCL.WalletProvider;
using Flow.Net.Sdk.Core;
using Flow.Net.Sdk.Core.Client;
using Flow.Net.Sdk.Core.Models;
using Flow.Net.SDK.Extensions;
using Newtonsoft.Json.Linq;

namespace Flow.FCL
{
    public class Transaction
    {
        private IWalletProvider _walletProvider;
        
        private IWebRequestUtils _webRequestUtils;
        
        private IResolveUtility _resolveUtility;
        
        private IFlowClient _flowClient;
        
        private string _testScript = "import FungibleToken from 0x9a0766d93b6608b7\nimport FlowToken from 0x7e60df042a9c0868\n\ntransaction(amount: UFix64, to: Address) {\n\n    // The Vault resource that holds the tokens that are being transferred\n    let sentVault: @FungibleToken.Vault\n\n    prepare(signer: AuthAccount) {\n\n        // Get a reference to the signer's stored vault\n        let vaultRef = signer.borrow<&FlowToken.Vault>(from: /storage/flowTokenVault)\n            ?? panic(\"Could not borrow reference to the owner's Vault!\")\n\n        // Withdraw tokens from the signer's stored vault\n        self.sentVault <- vaultRef.withdraw(amount: amount)\n    }\n\n    execute {\n\n        // Get the recipient's public account object\n        let recipient = getAccount(to)\n\n        // Get a reference to the recipient's Receiver\n        let receiverRef = recipient.getCapability(/public/flowTokenReceiver)\n            .borrow<&{FungibleToken.Receiver}>()\n            ?? panic(\"Could not borrow receiver reference to the recipient's Vault\")\n\n        // Deposit the withdrawn tokens in the recipient's receiver\n        receiverRef.deposit(from: <-self.sentVault)\n    }\n}";
        
        private string _txId;
        
        public Transaction(IWalletProvider walletProvider, IFlowClient flowClient, IResolveUtility resolveUtility, UtilFactory utilFactory)
        {
            _walletProvider = walletProvider;
            _flowClient = flowClient;
            _webRequestUtils = utilFactory.CreateWebRequestUtil();
            _resolveUtility = utilFactory.CreateResolveUtility();
        }
        
        public virtual void SendTransaction(string preAuthzUrl, FlowTransaction tx, Action internalCallback, Action<string> callback = null)
        {
            // var lastBlock = _flowClient.GetLatestBlockAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            // tx.ReferenceBlockId = lastBlock.Header.Id;
            // _walletProvider.SendTransaction(preAuthzUrl, tx, internalCallback, callback);
            var lastBlock = _flowClient.GetLatestBlockAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            tx.ReferenceBlockId = lastBlock.Header.Id;
            
            var preSignableJObj = _resolveUtility.ResolvePreSignable(ref tx);
            var preAuthzResponse = _webRequestUtils.GetResponse<PreAuthzAdapterResponse>(preAuthzUrl, "POST", "application/json", preSignableJObj);
            var isNonCustodial = preAuthzResponse.AuthorizerData.Authorizations.Any(p => p.Endpoint.ToLower().Contains("cosigner") || p.Endpoint.ToLower().Contains("non-custodial"));
            var tmpAccount = GetAccount(preAuthzResponse.AuthorizerData.Proposer.Identity.Address).ConfigureAwait(false).GetAwaiter().GetResult();
            tx.ProposalKey = GetProposerKey(tmpAccount, preAuthzResponse.AuthorizerData.Proposer.Identity.KeyId);
            
            var signableJObj = default(JObject);
            var endpoint = default((string IframeUrl, Uri PollingUrl));
            if(isNonCustodial)
            {
                var authorization = preAuthzResponse.AuthorizerData.Authorizations.First();
                var authorize = authorization.ConvertToFlowAccount();
                var signableJObjs = _resolveUtility.ResolveSignable(ref tx, preAuthzResponse.AuthorizerData, authorize);
                tx.PayloadSignatures.Clear();
            
                for (var index = 0; index < preAuthzResponse.AuthorizerData.Authorizations.Count; index++)
                {
                    signableJObj = signableJObjs[index];
                    var postUrl = preAuthzResponse.AuthorizerData.Authorizations[index].AuthzAdapterEndpoint();
                    var path = postUrl.Split("?").First().Split("/").Last();
                    switch (path)
                    {
                        case "cosigner":
                            var cosigner = _webRequestUtils.GetResponse<SignatureResponse>(postUrl, "POST", "application/json", signableJObj);
                            tx.PayloadSignatures.Add(new FlowSignature
                                                     {
                                                         Address = new FlowAddress(cosigner.SignatureInfo().Address.ToString()),
                                                         Signature = cosigner.SignatureInfo().Signature.ToString().StringToBytes().ToArray(),
                                                         KeyId = Convert.ToUInt32(cosigner.SignatureInfo().KeyId) 
                                                     });
                            break;
                        case "non-custodial":
                            var authzResponse = _webRequestUtils.GetResponse<NonCustodialAuthzResponse>(postUrl, "POST", "application/json", signableJObj);
                            endpoint = authzResponse.AuthzEndpoint();
                            break;
                    }
                }
                
                _walletProvider.Authz<SignatureResponse>(endpoint.IframeUrl, endpoint.PollingUrl, response => {
                                                                                                         var signInfo = response.SignatureInfo();
                                                                                                         if (signInfo.Signature != null)
                                                                                                         {
                                                                                                             $"Signature info keyId: {signInfo.KeyId}".ToLog();
                                                                                                             tx.PayloadSignatures.Add(new FlowSignature
                                                                                                                                      {
                                                                                                                                          //// wait frontend fix bug
                                                                                                                                          Address = new FlowAddress(authorization.Identity.Address),
                                                                                                                                          Signature = signInfo.Signature.ToString().StringToBytes().ToArray(),
                                                                                                                                          KeyId = Convert.ToUInt32(signInfo.KeyId)
                                                                                                                                      });
                                                                                                         }
            
                                                                                                         var payerEndpoint = preAuthzResponse.PayerEndpoint();
                                                                                                         var payerSignable = _resolveUtility.ResolvePayerSignable(ref tx, signableJObj);
                                                                                                         var payerSignResponse = _webRequestUtils.GetResponse<SignatureResponse>(payerEndpoint.AbsoluteUri, "POST", "application/json", payerSignable);
                                                                                                         signInfo = payerSignResponse.SignatureInfo();
                                                                                                         if (signInfo.Signature != null && signInfo.Address != null)
                                                                                                         {
                                                                                                             var envelopeSignature = tx.EnvelopeSignatures.First(p => p.Address.Address == signInfo.Address.ToString().RemoveHexPrefix());
                                                                                                             envelopeSignature.Signature = signInfo.Signature?.ToString().StringToBytes().ToArray();
                                                                                                         }
                                                                                                         
                                                                                                         var txResponse = _flowClient.SendTransactionAsync(tx).ConfigureAwait(false).GetAwaiter().GetResult();
                                                                                                         $"TxId: {txResponse.Id}".ToLog();
                                                                                                         callback?.Invoke(txResponse.Id);
                                                                                                     });  
            }
            else
            {
                var authorization = preAuthzResponse.AuthorizerData.Authorizations.First();
                var postUrl = authorization.AuthzAdapterEndpoint();
                var authorize = authorization.ConvertToFlowAccount();
                signableJObj = _resolveUtility.ResolveSignable(ref tx, preAuthzResponse.AuthorizerData, authorize).First();
                var authzResponse = _webRequestUtils.GetResponse<AuthzAdapterResponse>(postUrl, "POST", "application/json", signableJObj);
                endpoint = authzResponse.AuthzEndpoint();
                
                _walletProvider.Authz<AuthzAdapterResponse>(endpoint.IframeUrl, endpoint.PollingUrl, item => {
                                                                                                         var response = item as AuthzAdapterResponse;
                                                                                                         var signInfo = response.SignatureInfo();
                                                                                                         if (signInfo.Signature != null)
                                                                                                         {
                                                                                                             var payloadSignature = tx.PayloadSignatures.First(p => p.Address.Address == signInfo.Address?.ToString().RemoveHexPrefix());
                                                                                                             payloadSignature.Signature = signInfo.Signature?.ToString().StringToBytes().ToArray();
                                                                                                         }
            
                                                                                                         var payerEndpoint = preAuthzResponse.PayerEndpoint();
                                                                                                         var payerSignable = _resolveUtility.ResolvePayerSignable(ref tx, signableJObj);
                                                                                                         var payerSignResponse = _webRequestUtils.GetResponse<SignatureResponse>(payerEndpoint.AbsoluteUri, "POST", "application/json", payerSignable);
                                                                                                         signInfo = payerSignResponse.SignatureInfo();
                                                                                                         if (signInfo.Signature != null && signInfo.Address != null)
                                                                                                         {
                                                                                                             var envelopeSignature = tx.EnvelopeSignatures.First(p => p.Address.Address == signInfo.Address.ToString().RemoveHexPrefix());
                                                                                                             envelopeSignature.Signature = signInfo.Signature?.ToString().StringToBytes().ToArray();
                                                                                                         }
            
                                                                                                         var txResponse = _flowClient.SendTransactionAsync(tx).ConfigureAwait(false).GetAwaiter().GetResult();
                                                                                                         $"TxId: {txResponse.Id}".ToLog();
                                                                                                         callback?.Invoke(txResponse.Id);
                                                                                                     }); 
            }
        }

        public FlowTransactionResult GetTransactionStatus(string transactionId)
        {
            var txr = _flowClient.GetTransactionResultAsync(transactionId).ConfigureAwait(false).GetAwaiter().GetResult();
            return txr;
        }
        
        public async Task<FlowAccount> GetAccount(string address)
        {
            var account = _flowClient.GetAccountAtLatestBlockAsync(address);
            return await account;
        }
        
        private FlowProposalKey GetProposerKey(FlowAccount account, uint keyId)
        {
            var proposalKey = account.Keys.First(p => p.Index == keyId);
            return new FlowProposalKey
                   {
                       Address = account.Address,
                       KeyId = keyId,
                       SequenceNumber = proposalKey.SequenceNumber
                   };
        }
    }
}