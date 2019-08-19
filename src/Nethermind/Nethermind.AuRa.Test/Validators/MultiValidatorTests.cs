using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nethermind.AuRa.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs.ChainSpecStyle;
using Nethermind.Dirichlet.Numerics;
using Nethermind.Evm;
using Nethermind.Logging;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using NUnit.Framework;

namespace Nethermind.AuRa.Test.Validators
{
    public class MultiValidatorTests
    {
        private AuRaParameters.Validator _validator;
        private IAuRaAdditionalBlockProcessorFactory _factory;
        private ILogManager _logManager;
        private IDictionary<long, IAuRaValidatorProcessor> _innerValidators;
        private Block _block;
        private ITransactionProcessor _transactionProcessor;

        [SetUp]
        public void SetUp()
        {
            _validator = GetValidator();
            _innerValidators = new SortedList<long, IAuRaValidatorProcessor>();
            _factory = Substitute.For<IAuRaAdditionalBlockProcessorFactory>();
            _logManager = Substitute.For<ILogManager>();
            _transactionProcessor = Substitute.For<ITransactionProcessor>();
            
            _factory.CreateValidatorProcessor(default, default)
                .ReturnsForAnyArgs(x =>
                {
                    var innerValidator = Substitute.For<IAuRaValidatorProcessor>();
                    _innerValidators[x.Arg<long?>() ?? 0] = innerValidator;
                    return innerValidator;
                });

            _block = new Block(
                new BlockHeader(
                    Keccak.Zero,
                    Keccak.Zero,
                    Address.Zero,
                    UInt256.One,
                    1,
                    0,
                    UInt256.One,
                    Array.Empty<byte>()
                    ), new BlockBody());
        }
        
        [Test]
        public void Throws_ArgumentNullException_on_empty_validator()
        {
            Action act = () => new MultiValidator(null, _factory, _logManager);
            act.Should().Throw<ArgumentNullException>();
        }
        
        [Test]
        public void Throws_ArgumentNullException_on_empty_validatorFactory()
        {
            Action act = () => new MultiValidator(_validator, null, _logManager);
            act.Should().Throw<ArgumentNullException>();
        }
        
        [Test]
        public void Throws_ArgumentNullException_on_empty_logManager()
        {
            Action act = () => new MultiValidator(_validator,_factory,null);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void Throws_ArgumentException_on_wrong_validator_type()
        {
            _validator.ValidatorType = AuRaParameters.ValidatorType.Contract;
            Action act = () => new MultiValidator(_validator, _factory, _logManager);
            act.Should().Throw<ArgumentException>();
        }
        
        [Test]
        public void Throws_ArgumentException_on_empty_inner_validators()
        {
            _validator.Validators.Clear();
            Action act = () => new MultiValidator(_validator, _factory, _logManager);            
            act.Should().Throw<ArgumentException>();
        }
        
        [Test]
        public void Creates_inner_validators()
        {
            new MultiValidator(_validator, _factory, _logManager);
            _innerValidators.Keys.Should().BeEquivalentTo(_validator.Validators.Keys.Select(x => Math.Max(x, 1)));
        }
        
        [Test]
        public void Correctly_consecutively_calls_inner_validators()
        {
            // Arrange
            var validator = new MultiValidator(_validator, _factory, _logManager);
            var innerValidatorsFirstBlockCalls = GetInnerValidatorsFirstBlockCalls(_validator);
            var maxCalls = innerValidatorsFirstBlockCalls.Max() + 10;
            
            // Act
            ProcessBlocks(maxCalls, validator);

            // Assert
            var callCountPerValidator = innerValidatorsFirstBlockCalls.Zip(
                innerValidatorsFirstBlockCalls.Skip(1).Union(new[] {maxCalls}), (b0, b1) => (int)(b1 - b0))
                .ToArray();
            
            EnsureInnerValidatorsCalled(i => (_innerValidators[innerValidatorsFirstBlockCalls[i]], callCountPerValidator[i]));
        }

        [Test]
        public void Doesnt_call_inner_validators_before_start_block()
        {
            // Arrange
            _validator.Validators.Remove(0);
            var validator = new MultiValidator(_validator, _factory, _logManager);
            
            // Act
            ProcessBlocks(_validator.Validators.Keys.Min(), validator);

            // Assert
            EnsureInnerValidatorsCalled(i => (_innerValidators.ElementAt(i).Value, 0));
        }

        [Test]
        public void First_call_jumps_to_right_validator()
        {
            _validator.Validators.Remove(0);
            var validator = new MultiValidator(_validator, _factory, _logManager);
            var validatorsKeys = _validator.Validators.Keys;
            _block.Number = (validatorsKeys.First() + validatorsKeys.Skip(1).First()) / 2;
            
            validator.PreProcess(_block, _transactionProcessor);
            
            _innerValidators.First().Value.Received(1).PreProcess(Arg.Any<Block>(), _transactionProcessor);
        }
        
        private void ProcessBlocks(long count, MultiValidator validator)
        {
            for (int i = 1; i < count; i++)
            {
                _block.Number = i;
                validator.PreProcess(_block, _transactionProcessor);
                validator.PostProcess(_block, Array.Empty<TxReceipt>(), _transactionProcessor);
                validator.IsValidSealer(Address.Zero);
            }
        }
        
        private void EnsureInnerValidatorsCalled(Func<int, (IAuRaValidatorProcessor Validator, int calls)> getValidatorWithCallCount)
        {
            for (var i = 0; i < _innerValidators.Count; i++)
            {
                var (innerValidator, calls) = getValidatorWithCallCount(i);
                
                innerValidator.Received(calls).PreProcess(Arg.Any<Block>(), _transactionProcessor);
                innerValidator.Received(calls).PostProcess(Arg.Any<Block>(),
                    Array.Empty<TxReceipt>(), _transactionProcessor);
                innerValidator.Received(calls).IsValidSealer(Address.Zero);
            }
        }
        
        private long[] GetInnerValidatorsFirstBlockCalls(AuRaParameters.Validator validator)
        {
            return validator.Validators.Keys.Select(x => Math.Max(x, 1)).OrderBy(k => k).ToArray();
        }
        
        private static AuRaParameters.Validator GetValidator()
        {
            return new AuRaParameters.Validator()
            {
                ValidatorType = AuRaParameters.ValidatorType.Multi,
                Validators = new SortedList<long, AuRaParameters.Validator>()
                {
                    {
                        0,
                        new AuRaParameters.Validator()
                        {
                            ValidatorType = AuRaParameters.ValidatorType.List, 
                            Addresses = new[] {Address.FromNumber(0)}
                        }
                    },
                    {
                        10,
                        new AuRaParameters.Validator()
                        {
                            ValidatorType = AuRaParameters.ValidatorType.List, 
                            Addresses = new[] {Address.FromNumber(10)}
                        }
                    },
                    {
                        20,
                        new AuRaParameters.Validator()
                        {
                            ValidatorType = AuRaParameters.ValidatorType.List, 
                            Addresses = new[] {Address.FromNumber(20)}
                        }
                    },
                }
            };
        }
    }
}